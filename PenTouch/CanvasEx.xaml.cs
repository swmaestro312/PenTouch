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
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Shapes;

using Util;

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
 */

namespace PenTouch
{
	public sealed partial class CanvasEx : Grid
	{
		RectangleGeometry clipRect = new RectangleGeometry();

		InkManager inkManager;
		InkDrawingAttributes attr = new InkDrawingAttributes();
		uint pointID;
		Point prevPoint;

		//Util.MemoryRandomAccessStream randomAccessStream;

		enum PointerType
		{
			None, Drawing, Moving, Scaling
		}
		
		PointerType type = PointerType.None;

		TranslateTransform translate;
		ScaleTransform scale;

		public CanvasEx()
		{
			this.InitializeComponent();

			//randomAccessStream = new Util.MemoryRandomAccessStream(new MemoryStream());

			Clip = clipRect;
		}

		private void OnLoaded(object sender, RoutedEventArgs e)
		{
			attr.IgnorePressure = false;
			attr.PenTip = PenTipShape.Circle;
			attr.Size = new Size(4, 4);
			attr.Color = Colors.Red;
			ResetInkManager();
			
			translate = new TranslateTransform();
			scale = new ScaleTransform();
			var group = new TransformGroup();
			group.Children.Add(translate);
			group.Children.Add(scale);
			canvas.RenderTransform = group;
		}

		private void ResetInkManager()
		{
			inkManager = new InkManager();
			inkManager.SetDefaultDrawingAttributes(attr);
		}

		private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
		{
			if (type != PointerType.None)
				return;

			// Get information about the pointer location.
			PointerPoint pt = e.GetCurrentPoint(canvas);

			// Accept input only from a pen or mouse with the left button pressed. 
			PointerDeviceType pointerDevType = e.Pointer.PointerDeviceType;
			if (pointerDevType == PointerDeviceType.Pen ||
					pointerDevType == PointerDeviceType.Mouse &&
					pt.Properties.IsLeftButtonPressed)
			{
				pointID = pt.PointerId;
				type = PointerType.Drawing;

				LiveRenderBegin(pt);

				// Pass the pointer information to the InkManager.
				inkManager.ProcessPointerDown(pt);

				e.Handled = true;
			}

			else if (pointerDevType == PointerDeviceType.Touch ||
					pt.Properties.IsRightButtonPressed)
			{
				// Process touch input
				pointID = pt.PointerId;
				type = PointerType.Moving;
				prevPoint = pt.Position;

				e.Handled = true;
			}
		}

		private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
		{
			PointerPoint pt = e.GetCurrentPoint(canvas);
			
			if (type == PointerType.Drawing
				&& e.Pointer.PointerId == pointID)
			{
				LiveRenderUpdate(pt);
				
				// Pass the pointer information to the InkManager.
				inkManager.ProcessPointerUpdate(pt);

				e.Handled = true;
			}
			else if (type == PointerType.Moving
				&& e.Pointer.PointerId == pointID)
			{
				// Process touch input
				translate.X += pt.Position.X - prevPoint.X;
				translate.Y += pt.Position.Y - prevPoint.Y;

				e.Handled = true;
			}

		}

		private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
		{
			PointerPoint pt = e.GetCurrentPoint(canvas);

			if (type == PointerType.Drawing
				&& e.Pointer.PointerId == pointID)
			{
				// Pass the pointer information to the InkManager. 
				inkManager.ProcessPointerUp(pt);

				BezierRender();

				e.Handled = true;
			}
			else if (type == PointerType.Moving
				&& e.Pointer.PointerId == pointID)
			{
				// Process touch input

				e.Handled = true;
			}

			type = PointerType.None;
		}
		
		private void LiveRenderBegin(PointerPoint pt)
		{
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
				StrokeThickness = pt.Properties.Pressure * attr.Size.Width * 2,
				//StrokeThickness = pt.Properties.Pressure * 40,
				Stroke = new SolidColorBrush(Colors.Red),
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
		
		private void Button_Click_1(object sender, RoutedEventArgs e)
		{
			liveRender.Children.Clear();
			bezierRender.Children.Clear();
			
			translate.X = 0;
			translate.Y = 0;

			scale.ScaleX = 1;
			scale.ScaleY = 1;
			
			ResetInkManager();
		}
		/*
		private async void BezierRender()
		{
			//await inkManager.SaveAsync(randomAccessStream);
			
			
			bitmap.SetSource(randomAccessStream);
			
			image.Margin = new Thickness(
				inkManager.BoundingRect.Left, 
				inkManager.BoundingRect.Top, 0, 0);

			image.Width = inkManager.BoundingRect.Width;
			image.Height = inkManager.BoundingRect.Height;
			*//*
			BitmapImage bmp = new BitmapImage();
			bmp.SetSource(randomAccessStream);

			Image img = new Image();
			img.Source = bmp;
			img.Margin = new Thickness(
				inkManager.BoundingRect.Left,
				inkManager.BoundingRect.Top, 0, 0);
			img.Width = inkManager.BoundingRect.Width;
			img.Height = inkManager.BoundingRect.Height;
			
			bezierRender.Children.Add(img);
			
			//ResetInkManager();
			
			LiveRenderEnd();
		}*/

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
					
					// Add path to render so that it is rendered (on top of all the elements with same ZIndex).
					// We want the live render to be on top of the Bezier render, so we set the ZIndex of the elements of the
					// live render to 2 and that of the elements of the Bezier render to 1.
					strokeCanvas.Children.Add(path);
					Windows.UI.Xaml.Controls.Canvas.SetZIndex(path, 1);

					org = segs.Current.Position;
				}

				bezierRender.Children.Add(strokeCanvas);
			}

			ResetInkManager();
			LiveRenderEnd();
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
			path.Stroke = new SolidColorBrush(attr.Color);
			path.StrokeThickness = pressure * attr.Size.Width * 2;
			path.StrokeStartLineCap = PenLineCap.Round;
			path.StrokeEndLineCap = PenLineCap.Round;
			path.StrokeLineJoin = PenLineJoin.Round;
			path.Data = geometry;

			return path;
		}

		private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
		{
			PointerPoint pt = e.GetCurrentPoint(canvas);
			Scaling(pt.Position, (pt.Properties.MouseWheelDelta > 0) ? 1.05 : 0.95);
		}

		private void OnManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
		{
			type = PointerType.Scaling;

			Scaling(e.Position, e.Delta.Scale);
		}

		private void Scaling(Point p, double value)
		{
			if (scale.ScaleX * value > 2.0f)
				value = 2.0f / scale.ScaleX;
			if (scale.ScaleX * value < 0.5f)
				value = 0.5f / scale.ScaleX;

			Point t = new Point(p.X + translate.X, p.Y + translate.Y);

			t.X *= (1 - value);
			t.Y *= (1 - value);

			translate.X += t.X;
			translate.Y += t.Y;

			scale.ScaleX *= value;
			scale.ScaleY = scale.ScaleX;
		}

		private void Button_Click_2(object sender, RoutedEventArgs e)
		{
			if (bezierRender.Children.Count > 0)
				bezierRender.Children.Remove(bezierRender.Children.Last());
		}
		
		/*
		public static Path CreateBezierPath(InkStroke stroke)
		{
			// Create Bezier geometries using information provided by the stroke's segments
			var figure = new PathFigure();
			var segments = stroke.GetRenderingSegments().GetEnumerator();
			
			segments.MoveNext();
			// First segment is degenerate and corresponds to initial position
			figure.StartPoint = segments.Current.Position;
			// Now loop through all remaining segments
			while (segments.MoveNext())
			{
				var bs = new BezierSegment();
				bs.Point1 = segments.Current.BezierControlPoint1;
				bs.Point2 = segments.Current.BezierControlPoint2;
				bs.Point3 = segments.Current.Position;
				figure.Segments.Add(bs);
			}

			// Create and initialize the data structures necessary to render the figure
			var geometry = new PathGeometry();
			geometry.Figures.Add(figure);
			var path = new Path();
			path.Data = geometry;
			
			// Set the stroke's graphical properties, which are controlled by the Path object
			path.Stroke = new Windows.UI.Xaml.Media.SolidColorBrush(stroke.DrawingAttributes.Color);
			path.StrokeThickness = stroke.DrawingAttributes.Size.Width;
			path.StrokeLineJoin = Windows.UI.Xaml.Media.PenLineJoin.Round;
			path.StrokeStartLineCap = Windows.UI.Xaml.Media.PenLineCap.Round;

			return path;
		}
		 * */
	}
}
