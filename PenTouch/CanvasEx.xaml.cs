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
//using Windows.Graphics.Imaging;

using Windows.UI;
using Windows.UI.Input;
using Windows.UI.Input.Inking;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Shapes;

// 사용자 정의 컨트롤 항목 템플릿에 대한 설명은 http://go.microsoft.com/fwlink/?LinkId=234236에 나와 있습니다.

namespace PenTouch
{
	public sealed partial class CanvasEx : Grid
	{
		InkManager inkManager = new InkManager();
		uint penID;

		BitmapImage bitmap = new BitmapImage();

		public CanvasEx()
		{
			this.InitializeComponent();
			image.Source = bitmap;
		}

		private void OnLoaded(object sender, RoutedEventArgs e)
		{
			var attr = new InkDrawingAttributes();
			attr.IgnorePressure = false;
			attr.PenTip = PenTipShape.Circle;
			attr.Size = new Size(4, 4);
			attr.Color = Colors.Black;
			attr.FitToCurve = true;
			inkManager.SetDefaultDrawingAttributes(attr);
		}

		private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
		{
			// Get information about the pointer location.
			PointerPoint pt = e.GetCurrentPoint(this);

			// Accept input only from a pen or mouse with the left button pressed. 
			PointerDeviceType pointerDevType = e.Pointer.PointerDeviceType;
			if (pointerDevType == PointerDeviceType.Pen ||
					pointerDevType == PointerDeviceType.Mouse &&
					pt.Properties.IsLeftButtonPressed)
			{
				// Pass the pointer information to the InkManager.
				inkManager.ProcessPointerDown(pt);
				penID = pt.PointerId;

				LiveRenderBegin(pt);

				e.Handled = true;
			}

			else if (pointerDevType == PointerDeviceType.Touch)
			{
				// Process touch input
			}
		}

		private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
		{
			if (e.Pointer.PointerId == penID)
			{
				PointerPoint pt = e.GetCurrentPoint(this);

				LiveRenderUpdate(pt);

				// Pass the pointer information to the InkManager.
				inkManager.ProcessPointerUpdate(pt);
			}
			/*
			else if (e.Pointer.PointerId == _touchID)
			{
				// Process touch input
			}
			*/
			e.Handled = true;
		}

		private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
		{
			if (e.Pointer.PointerId == penID)
			{
				PointerPoint pt = e.GetCurrentPoint(this);

				// Pass the pointer information to the InkManager. 
				inkManager.ProcessPointerUp(pt);
				
				//LiveRenderEnd();
				//BezierRender();
			}
			/*
			else if (e.Pointer.PointerId == _touchID)
			{
				// Process touch input
			}
			*/
			//_touchID = 0;
			penID = 0;

			e.Handled = true;
		}
		
		Point prevPoint;
		List<Line> liveRender = new List<Line>();

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
				StrokeThickness = pt.Properties.Pressure * 13 + 1,
				Stroke = new SolidColorBrush(Colors.Red),
				StrokeStartLineCap = PenLineCap.Round,
				StrokeEndLineCap = PenLineCap.Round,
				StrokeLineJoin = PenLineJoin.Round,
			};

			Children.Add(line);

			liveRender.Add(line);
			
			prevPoint = nowPoint;
		}

		private void LiveRenderEnd()
		{
			foreach (Line l in liveRender)
				Children.Remove(l);

			liveRender.Clear();
		}
		
		private void Button_Click_1(object sender, RoutedEventArgs e)
		{
			Children.Clear();
			Children.Add(sender as UIElement);
		}

		private async void BezierRender()
		{
			var stream = new InMemoryRandomAccessStream();
			await inkManager.SaveAsync(stream);
			bitmap.SetSource(stream);
		}

		/*
		private void BezierRender()
		{
			foreach (var stroke in inkManager.GetStrokes())
			{
				var segs = stroke.GetRenderingSegments().GetEnumerator();
				segs.MoveNext();

				Point org = segs.Current.Position;

				while (segs.MoveNext())
				{
					var path = CreateBezierPath(segs.Current.BezierControlPoint1, segs.Current.BezierControlPoint2, segs.Current.Position, org, segs.Current.Pressure);

					// Add path to render so that it is rendered (on top of all the elements with same ZIndex).
					// We want the live render to be on top of the Bezier render, so we set the ZIndex of the elements of the
					// live render to 2 and that of the elements of the Bezier render to 1.
					Children.Add(path);
					Windows.UI.Xaml.Controls.Canvas.SetZIndex(path, 1);

					org = segs.Current.Position;
				}
			}
		}
		
		public static Path CreateBezierPath(Point p1, Point p2, Point p3, Point org, float pressure)
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

			var path = new Path();
			path.Stroke = new SolidColorBrush(Colors.Blue);
			path.StrokeThickness = pressure * 30;
			path.StrokeStartLineCap = PenLineCap.Round;
			path.StrokeEndLineCap = PenLineCap.Round;
			path.StrokeLineJoin = PenLineJoin.Round;
			path.Data = geometry;

			return path;
		}
		*/
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
