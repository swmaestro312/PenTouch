using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.IO;

using Windows.Devices.Input; 
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Storage;
using Windows.Storage.Streams;

using Windows.UI;
using Windows.UI.Input;
using Windows.UI.Input.Inking;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Shapes;

// 사용자 정의 컨트롤 항목 템플릿에 대한 설명은 http://go.microsoft.com/fwlink/?LinkId=234236에 나와 있습니다.

/* Programming Note
 * 
 * 12 10 22
 * 원격 디버깅 설정을 했습니다.
 * 
 * 12 10 23 ~ 24
 * WPF 등에 있었던 WriteableBitmap.Render 등 비트맵으로의 출력이 삭제된 덕에 고생 좀 했습니다.
 * 그렇다고 벡터를 화면에 직접 출력하자니 너무 느린 등의 문제가 있었습니다.
 * 라인으로 출력하는 편이 그나마 퍼포먼스가 좋아보였지만, 선 중복 문제가..
 * 
 * 12 10 25
 * 딱 하나 InkManager에 파일로 저장하는 메소드가 있었습니다.
 * 해당 메소드에 InMemoryRandomAccessStream 객체를 넣어서 구현해보려 했지만 실패했습니다.
 * 이것저것 찾아보던 중 MemoryRandomAccessStream 이라는 키워드를 얻었고 한 블로거가 System.IO.MemoryStream을 포팅한 클래스의 포스팅을 찾을 수 있었습니다.
 * 해당 클래스를 이용해서 진행하면 제대로 작동합니다.
 * 이슈가 두 가지 발생했습니다.
 * 1. 현재는 선 겹침 문제가 해결되지 않았습니다. InkManager를 초기화해서 최대한 퍼포먼스를 좋게 만들어야 할 필요성.
 * 2. 실제 그려진 위치와 떨어진 위치에 이미지가 나타납니다. 그려진 부분만을 클리핑해서 파일로 만들기 때문에 그런 듯.
 * 차후 수정할 필요가 있습니다.
 * 
 * 12 10 26
 * 최대한 실제 그려진 위치와 일치한 곳에 벡터가 그려지도록 했습니다.
 * Image를 Fill 형태로 그려지게 하고 InkManager의 BoundingRect를 Image에 적용했습니다.
 * InkManager 를 남겨두는 편이 통신을 고려하면 더 좋을지도 모르겠습니다.
 * 터치를 통해 캔버스를 움직이는 기능울 구현하였습니다.
 * InkManager 를 남겨두니까 부하가 엄청 걸리네요.
 * 비트맵을 합치는 방법을 사용해야 할 듯.
 * 메모리 버퍼는 최대한 생성하지 않는 편이 나은 것 같습니다.
 * 
 * 12 10 27
 * Image를 여러 개 Child로 추가하는 편이 가장 퍼포먼스가 좋았습니다.
 * 겹쳤을 때 테두리가 하얗게 되는 현상을 확인하였습니다.
 * 
 * 12 10 28
 * Bezier를 렌더링 할 때 계속 겹쳐 그리고 있던 것을 발견하였습니다.
 * 그때마다 inkManager를 초기화하니 퍼포먼스가 좋아졌습니다. 오예.
 * 확대/축소를 구현하였습니다. 마우스/터치 모두 가능. 0.5배 ~ 2배까지
 * Ctrl-Z를 구현했습니다.
 * 
 * 12 10 29
 * Appbar를 추가했습니다.
 * 리팩토링을 하였습니다.
 * 색 변경 기능을 구현하였습니다.
 * 굵기 변경 기능을 구현하였습니다.
 * Scaling이 너무 느립니다. 보완하고 싶은데.. 
 * 
 * 12 10 30
 * Scaling 속도를 DoubleAnimation을 통해 개선하였습니다. 1.5배씩 확대 축소됨.
 * 기존 Translate > Scale을 Scale > Transform으로 바꾸고 핀치 투 줌을 다시 구현하였습니다.
 * 
 * 12 11 03 ~ 04
 * Network 연동
 */

namespace PenTouch
{
	public sealed partial class CanvasEx : Grid
	{
		RectangleGeometry clipRect = new RectangleGeometry();

		InkManager				inkManager;
		InkDrawingAttributes	inkAttr;

		enum ActionType
		{
			None, Drawing, Moving, Scaling
		}

		ActionType	actionType = ActionType.None;
		uint		pointID;
		Point		prevPoint;
		int			zoomLevel;

		public CanvasEx()
		{
			this.InitializeComponent();

			Clip = clipRect;
			zoomLevel = 0;
		}

		private void OnLoaded(object sender, RoutedEventArgs e)
		{
			inkAttr = new InkDrawingAttributes();
			inkAttr.IgnorePressure = false;
			inkAttr.PenTip = PenTipShape.Circle;
			inkAttr.Size = new Size(4, 4);
			inkAttr.Color = Colors.Black;
			ResetInkManager();

			Network.OnNetworkRecieved += OnNetworkRecieved;
			Network.connect();
		}

		private void ResetInkManager()
		{
			inkManager = new InkManager();
			inkManager.SetDefaultDrawingAttributes(inkAttr);
		}

		private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
		{
			if (actionType != ActionType.None)
				return;

			PointerPoint pt = e.GetCurrentPoint(canvas);
			PointerDeviceType pointerDevType = e.Pointer.PointerDeviceType;
			
			if (pointerDevType == PointerDeviceType.Pen ||
					pointerDevType == PointerDeviceType.Mouse &&
					pt.Properties.IsLeftButtonPressed)
			{
				LiveRenderBegin(pt);
				inkManager.ProcessPointerDown(pt);

				e.Handled = true;
			}

			else if (pointerDevType == PointerDeviceType.Touch ||
					pt.Properties.IsRightButtonPressed)
			{
				pointID = pt.PointerId;
				actionType = ActionType.Moving;
				prevPoint = pt.Position;

				e.Handled = true;
			}
		}

		private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
		{
			PointerPoint pt = e.GetCurrentPoint(canvas);
			
			if (actionType == ActionType.Drawing && e.Pointer.PointerId == pointID)
			{
				LiveRenderUpdate(pt);
				inkManager.ProcessPointerUpdate(pt);

				e.Handled = true;
			}
			else if (actionType == ActionType.Moving && e.Pointer.PointerId == pointID)
			{
				translate.X += (pt.Position.X - prevPoint.X) * scale.ScaleX;
				translate.Y += (pt.Position.Y - prevPoint.Y) * scale.ScaleY;
				e.Handled = true;
			}
		}

		private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
		{
			PointerPoint pt = e.GetCurrentPoint(canvas);

			if (actionType == ActionType.Drawing && e.Pointer.PointerId == pointID)
			{
				inkManager.ProcessPointerUp(pt);
				BezierRender();
				actionType = ActionType.None;

				e.Handled = true;
			}
			else if (actionType == ActionType.Moving && e.Pointer.PointerId == pointID)
			{
				actionType = ActionType.None;

				e.Handled = true;
			}
		}
		
		private void LiveRenderBegin(PointerPoint pt)
		{
			actionType = ActionType.Drawing;

			pointID = pt.PointerId;
			prevPoint = pt.Position;
		}

		private void LiveRenderUpdate(PointerPoint pt)
		{
			Point nowPoint = pt.Position;

			Line line = new Line()
			{
				X1 = prevPoint.X,
				Y1 = prevPoint.Y,
				X2 = nowPoint.X,
				Y2 = nowPoint.Y,
				StrokeThickness = pt.Properties.Pressure * inkAttr.Size.Width * 2,
				Stroke = new SolidColorBrush(inkAttr.Color),
				StrokeStartLineCap = PenLineCap.Round,
				StrokeEndLineCap = PenLineCap.Round,
				StrokeLineJoin = PenLineJoin.Round,
			};

			liveRender.Children.Add(line);
			
			prevPoint = nowPoint;
		}

		private void LiveRenderEnd()
		{
			liveRender.Children.Clear();
		}

		private void OnSizeChanged(object sender, SizeChangedEventArgs e)
		{
			clipRect.Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height);
		}
		
		private void BezierRender()
		{
			foreach (var stroke in inkManager.GetStrokes())
			{
				var strokeCanvas = new Canvas();

				var segs = stroke.GetRenderingSegments().GetEnumerator();
				segs.MoveNext();

				Point org = segs.Current.Position;

				while (segs.MoveNext())
				{
					var path = CreateBezierPath(segs.Current.BezierControlPoint1, segs.Current.BezierControlPoint2, segs.Current.Position, org, segs.Current.Pressure);
					Network.sendData(segs.Current.BezierControlPoint1, segs.Current.BezierControlPoint2, segs.Current.Position, org, segs.Current.Pressure);
					
					strokeCanvas.Children.Add(path);
					Windows.UI.Xaml.Controls.Canvas.SetZIndex(path, 1);

					org = segs.Current.Position;
				}

				bezierRender.Children.Add(strokeCanvas);
			}

			ResetInkManager();
			LiveRenderEnd();
		}

		private void OnNetworkRecieved(Point p1, Point p2, Point p3, Point p4, float pressure)
		{
			var path = CreateBezierPath(p1, p2, p3, p4, pressure);
			bezierRender.Children.Add(path);
		}
		
		public Windows.UI.Xaml.Shapes.Path CreateBezierPath(Point p1, Point p2, Point p3, Point org, float pressure)
		{
			var figure = new PathFigure();
			figure.StartPoint = org;
			
			var bezier = new BezierSegment();
			bezier.Point1 = p1;
			bezier.Point2 = p2;
			bezier.Point3 = p3;
			figure.Segments.Add(bezier);

			var geometry = new PathGeometry();
			geometry.Figures.Add(figure);

			var path = new Windows.UI.Xaml.Shapes.Path();
			path.Stroke = new SolidColorBrush(inkAttr.Color);
			path.StrokeThickness = pressure * inkAttr.Size.Width * 2;
			path.StrokeStartLineCap = PenLineCap.Round;
			path.StrokeEndLineCap = PenLineCap.Round;
			path.StrokeLineJoin = PenLineJoin.Round;
			path.Data = geometry;

			return path;
		}

		private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
		{
			PointerPoint pt = e.GetCurrentPoint(canvas);
			Debug.WriteLine(pt.Properties.MouseWheelDelta);
			Scaling(pt.Position, pt.Properties.MouseWheelDelta > 0);
			e.Handled = true;
		}

		private void OnManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
		{
			Scaling(e.Position, e.Delta.Scale > 0);
			e.Handled = true;
		}

		private void Scaling(Point p, bool zoomin)
		{
			if (actionType == ActionType.Scaling)
				return;

			double value = 1.5;
			if (zoomin)
			{
				if (zoomLevel > 1)
					return;

				++zoomLevel;
			}
			else
			{
				if (zoomLevel < -1)
					return;
				
				--zoomLevel;
				value = 1 / value;
			}

			actionType = ActionType.Scaling;

			Point t = new Point(p.X * scale.ScaleX + translate.X, p.Y * scale.ScaleX + translate.Y);

			t.X -= t.X * (1 / value);
			t.Y -= t.Y * (1 / value);
			/*
			scale.ScaleX *= value;
			scale.ScaleY *= value;

			translate.X -= t.X;
			translate.Y -= t.Y;

			translate.X *= value;
			translate.Y *= value;
			/*/
			animTranslateX.To = (translate.X - t.X) * value;
			animTranslateY.To = (translate.Y - t.Y) * value;

			animScaleX.To = scale.ScaleX * value;
			animScaleY.To = scale.ScaleY * value;
			
			storyboard.Begin();
			//*/
		}

		private void OnAnimCompleted(object sender, object e)
		{
			actionType = ActionType.None;
		}

		public void Clear()
		{
			liveRender.Children.Clear();
			bezierRender.Children.Clear();

			translate.X = 0;
			translate.Y = 0;

			scale.ScaleX = 1;
			scale.ScaleY = 1;

			ResetInkManager();
		}

		public void Undo()
		{
			if (bezierRender.Children.Count > 0)
				bezierRender.Children.Remove(bezierRender.Children.Last());
		}

		public void ChangePenColor(Color color)
		{
			if (inkAttr == null)
				return; 

			inkAttr.Color = color;
			ResetInkManager();
		}

		public void ChangePenThickness(double value)
		{
			if (inkAttr == null)
				return;

			inkAttr.Size = new Size(value, value);
			ResetInkManager();
		}
	}
}
