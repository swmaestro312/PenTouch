using System;
using System.Diagnostics;
using System.Linq;

using Windows.Devices.Input;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Input;
using Windows.UI.Input.Inking;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Shapes;

/*
 * 12 11 05~06
 * 퍼포먼스를 해결해야 합니다.
 * 리팩토링
 * 
 * 12 11 07
 * 라인 두께, 길이 제한 설정
 * 팜블락 반대방향도 가능하게 설정
 * 터치 캔슬 펜 설정
 */

namespace PenTouch
{
	public sealed partial class CanvasEx : Grid
	{
		RectangleGeometry clipRect = new RectangleGeometry();

		Canvas liveRender = null;

		double strokeThickness = 4;
		Brush strokeColor = new SolidColorBrush(Colors.Black);

		Polygon palmBlock;
		int		palmSide;
		Line	palmTempLine;

		uint		pointID;
		Point		pointPrev;
		
		public CanvasEx()
		{
			this.InitializeComponent();

			palmBlock = new Polygon();
			rootCanvas.Children.Add(palmBlock);
			palmBlock.StrokeThickness = 3;
			palmBlock.FillRule = FillRule.Nonzero;
			palmBlock.Fill = new SolidColorBrush(Color.FromArgb(0x22, 0xff, 0, 0));

			Clip = clipRect;
		}

		private void OnLoaded(object sender, RoutedEventArgs e)
		{
			ManipulationMode = ManipulationModes.TranslateX | ManipulationModes.TranslateY | ManipulationModes.TranslateInertia;
			onePointWait.Visibility = Visibility.Collapsed;
			
			penTouchTimer = new DispatcherTimer();
			penTouchTimer.Interval = new TimeSpan(0, 0, 1);
			penTouchTimer.Tick += PenTouchEnd;

			double val = -Math.PI / 2;
			foreach (UIElement child in colorSelect.Children)
			{
				child.SetValue(Canvas.LeftProperty, Math.Cos(val) * 70);
				child.SetValue(Canvas.TopProperty, Math.Sin(val) * 70);
				val += Math.PI / 5;
			}
		}

		#region Util
		
		private double Distance(Point p1, Point p2)
		{
			return Math.Sqrt(Math.Pow(p1.X - p2.X, 2) + Math.Pow(p1.Y - p2.Y, 2));
		}

		private double Angle(Point p1, Point p2)
		{
			return Math.Atan2(p2.X - p1.X, p2.Y - p1.Y);
		}

		private bool PointInPalmBlock(Point p)
		{
			if (palmBlock.Visibility == Visibility.Collapsed)
				return false;

			int i, j = palmBlock.Points.Count - 1;
			bool oddNodes = false;

			for (i = 0; i < palmBlock.Points.Count; i++)
			{
				if ((palmBlock.Points[i].Y < p.Y && palmBlock.Points[j].Y >= p.Y
				|| palmBlock.Points[j].Y < p.Y && palmBlock.Points[i].Y >= p.Y)
				&& (palmBlock.Points[i].X <= p.X || palmBlock.Points[j].X <= p.X))
				{
					oddNodes ^= (palmBlock.Points[i].X + (p.Y - palmBlock.Points[i].Y) / (palmBlock.Points[j].Y - palmBlock.Points[i].Y) * (palmBlock.Points[j].X - palmBlock.Points[i].X) < p.X);
				}

				j = i;
			}

			return oddNodes;
		}

		#endregion

		#region Action

		enum ActionType
		{
			None, Wait, Draw, Move, Palm, PenTouch
		}

		ActionType actionType;

		#region Palm Block

		public void PalmBlockSelect()
		{
			actionType = ActionType.Palm;
			palmBlock.Points.Clear();
		}

		//pt = e.GetCurrentPoint(palmBlock)
		private bool PalmBlockStart(PointerPoint pt)
		{
			if (actionType != ActionType.Palm)
				return false;

			palmBlock.Points.Clear();
			palmBlock.Points.Add(new Point(0, 0));
			palmBlock.Points.Add(pt.Position);

			palmTempLine = new Line();
			palmTempLine.StrokeDashArray.Add(5);
			palmTempLine.StrokeDashArray.Add(5);
			palmTempLine.Stroke = new SolidColorBrush(Colors.Black);
			palmTempLine.Margin = palmBlock.Margin;
			rootCanvas.Children.Add(palmTempLine);

			pointID = pt.PointerId;
			pointPrev = pt.Position;
			palmSide = 0;

			return true;
		}
		
		//pt = e.GetCurrentPoint(palmBlock)
		private bool PalmBlockMove(PointerPoint pt)
		{
			if (actionType != ActionType.Palm ||
				pointID != pt.PointerId ||
				palmTempLine == null)
				return false;

			if (palmSide == 0 && Distance(pointPrev, pt.Position) < 5)
				return true;

			bool angle = Angle(new Point(0, 0), palmBlock.Points[palmBlock.Points.Count - 1]) > Angle(new Point(0, 0), pt.Position);

			if (angle && palmSide >= 0)
			{
				if (palmSide == 0)
				{
					palmSide = 1;
					palmBlock.Points.Insert(1, new Point(pt.Position.X, 0));
				}

				//CW
				palmTempLine.X1 = pt.Position.X;
				palmTempLine.Y1 = pt.Position.Y;
				palmTempLine.X2 = 0;
				palmTempLine.Y2 = pt.Position.Y;

				palmBlock.Points.Add(pt.Position);
			}
			else if (!angle && palmSide <= 0)
			{
				if (palmSide == 0)
				{
					palmSide = -1;
					palmBlock.Points.Insert(1, new Point(0, pt.Position.Y));
				}

				//CCW
				palmTempLine.X1 = pt.Position.X;
				palmTempLine.Y1 = pt.Position.Y;
				palmTempLine.X2 = pt.Position.X;
				palmTempLine.Y2 = 0;

				palmBlock.Points.Add(pt.Position);
			}

			return true;
		}

		//pt = e.GetCurrentPoint(palmBlock)
		private bool PalmBlockEnd(PointerPoint pt)
		{
			if (actionType != ActionType.Palm ||
				pointID != pt.PointerId ||
				palmTempLine == null)
				return false;
			
			if (palmSide != 0)
			{
				rootCanvas.Children.Remove(palmTempLine);
				palmBlock.Points.Add(new Point(palmTempLine.X2, palmTempLine.Y2));
			}

			actionType = ActionType.None;
			palmTempLine = null;
			
			return true;
		}

		#endregion

		#region Drawing

		//pt = e.GetCurruntPoint(canvas);
		private bool DrawStart(PointerPoint pt)
		{
			if (actionType != ActionType.None && actionType != ActionType.PenTouch ||
				pt.PointerDevice.PointerDeviceType != PointerDeviceType.Pen)
				return false;

			PenTouchEnd();

			actionType = ActionType.Draw;

			pointID = pt.PointerId;
			pointPrev = pt.Position;

			liveRender = new Canvas();
			canvas.Children.Add(liveRender);

			return true;
		}

		//pt = e.GetCurruntPoint(canvas);
		private bool DrawMove(PointerPoint pt)
		{
			if (actionType != ActionType.Draw ||
				pt.PointerDevice.PointerDeviceType != PointerDeviceType.Pen ||
				pt.PointerId != pointID ||
				liveRender == null)
				return false;

			Point nowPoint = pt.Position;

			Line line = new Line()
			{
				X1 = pointPrev.X,
				Y1 = pointPrev.Y,
				X2 = nowPoint.X,
				Y2 = nowPoint.Y,
				StrokeThickness = pt.Properties.Pressure * strokeThickness,
				Stroke = strokeColor,
				StrokeStartLineCap = PenLineCap.Round,
				StrokeEndLineCap = PenLineCap.Round,
				StrokeLineJoin = PenLineJoin.Round,
			};

			liveRender.Children.Add(line);

			pointPrev = nowPoint;

			return true;
		}

		//pt = e.GetCurruntPoint(canvas);
		private bool DrawEnd(PointerPoint pt)
		{
			if (actionType != ActionType.Draw ||
				pt.PointerDevice.PointerDeviceType != PointerDeviceType.Pen ||
				pointID != pt.PointerId ||
				liveRender == null)
				return false;

			actionType = ActionType.None;
			liveRender = null;

			return true;
		}

		#endregion Drawing

		#region Waiting

		DispatcherTimer penTouchTimer;

		//pt = e.GetCurruntPoint(rootCanvas);
		private bool WaitStart(PointerPoint pt)
		{
			if (actionType != ActionType.None && actionType != ActionType.PenTouch ||
				pt.PointerDevice.PointerDeviceType != PointerDeviceType.Touch)
				return false;

			if (PointInPalmBlock(new Point(pt.Position.X - palmBlock.Margin.Left, pt.Position.Y - palmBlock.Margin.Top)))
				return false;

			actionType = ActionType.Wait;
			pointID = pt.PointerId;
			pointPrev = pt.Position;

			onePointWait.Visibility = Visibility.Visible;
			onePointWait.Opacity = 0.5;
			onePointWait.Margin = new Thickness(pt.Position.X, pt.Position.Y, 0, 0);

			colorSelect.Visibility = Visibility.Collapsed;
			onePointButtons.Visibility = Visibility.Visible;

			return true;
		}

		private bool MovingMove(Point delta)
		{
			if (actionType != ActionType.Wait && actionType != ActionType.Move)
				return false;

			if (actionType == ActionType.Wait)
				if (Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y) < 5)
					return false;
				else
				{
					PenTouchEnd();
					actionType = ActionType.Move;
				}

			transform.X += delta.X;
			transform.Y += delta.Y;

			return true;
		}
		
		//pt = e.GetCurruntPoint(rootCanvas);
		private bool WaitEnd(PointerPoint pt)
		{
			if (actionType != ActionType.Wait ||
				pt.PointerDevice.PointerDeviceType != PointerDeviceType.Touch ||
				pt.PointerId != pointID)
				return false;

			actionType = ActionType.None;

			onePointWait.Visibility = Visibility.Collapsed;
			
			return true;
		}

		//pt = e.GetCurruntPoint(rootCanvas);
		private bool PenTouchStart(PointerPoint pt)
		{
			if (actionType != ActionType.Wait ||
				pt.PointerDevice.PointerDeviceType != PointerDeviceType.Touch ||
				pt.PointerId != pointID)
				return false;

			actionType = ActionType.PenTouch;

			onePointWait.Opacity = 1;

			penTouchTimer.Start();

			return true;
		}

		private void PenTouchEnd(object sender = null, object e = null)
		{
			penTouchTimer.Stop();

			if (actionType == ActionType.PenTouch)
				actionType = ActionType.None;
	
			onePointWait.Visibility = Visibility.Collapsed;
		}

		private void OnColorSelectPointerPressed(object sender, PointerRoutedEventArgs e)
		{
			if (e.Pointer.PointerDeviceType != PointerDeviceType.Pen)
				return;

			e.Handled = true;

			penTouchTimer.Stop();
			onePointButtons.Visibility = Visibility.Collapsed;
			colorSelect.Visibility = Visibility.Visible;
			//penTouchType = PenTouchType.ColorSelect;
		}

		private void OnColorSelectPointerReleased(object sender, PointerRoutedEventArgs e)
		{
			if (e.Pointer.PointerDeviceType != PointerDeviceType.Pen)
				return;

			Shape s = sender as Shape;

			if (s == null)
				return;

			e.Handled = true;

			ChangePenColor(s.Fill);

			PenTouchEnd();
		}

		#endregion Wating

		#endregion Action

		private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
		{
			e.Handled = true;

			if (PalmBlockStart(e.GetCurrentPoint(palmBlock)))
				return;

			if (DrawStart(e.GetCurrentPoint(canvas)))
				return;
			
			if (WaitStart(e.GetCurrentPoint(rootCanvas)))
				return;
		}

		private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
		{
			e.Handled = true;

			if (PalmBlockMove(e.GetCurrentPoint(palmBlock)))
				return;

			if (DrawMove(e.GetCurrentPoint(canvas)))
				return;
		}

		private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
		{
			e.Handled = true;

			if (PalmBlockEnd(e.GetCurrentPoint(palmBlock)))
				return;

			if (DrawEnd(e.GetCurrentPoint(canvas)))
				return;

			if (WaitEnd(e.GetCurrentPoint(rootCanvas)))
				return;
			
			if (e.Pointer.PointerDeviceType == PointerDeviceType.Pen)
				PenTouchEnd();
		}

		private void OnPointerCanceled(object sender, PointerRoutedEventArgs e)
		{
			e.Handled = true;

			if (PalmBlockEnd(e.GetCurrentPoint(palmBlock)))
				return;

			if (DrawEnd(e.GetCurrentPoint(canvas)))
				return;

			if (PenTouchStart(e.GetCurrentPoint(rootCanvas)))
				return;

			if (e.Pointer.PointerDeviceType == PointerDeviceType.Pen)
				PenTouchEnd();
		}

		private void OnSizeChanged(object sender, SizeChangedEventArgs e)
		{
			palmBlock.Margin = new Thickness(e.NewSize.Width, e.NewSize.Height, 0, 0);
			clipRect.Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height);

			if (ApplicationView.Value == ApplicationViewState.Snapped)
				palmBlock.Visibility = Visibility.Collapsed;
			else
				palmBlock.Visibility = Visibility.Visible;
		}

		private void OnAnimCompleted(object sender, object e)
		{
			actionType = ActionType.None;
		}

		public void Clear()
		{
			canvas.Children.Clear();
		}

		public void Undo()
		{
			if (canvas.Children.Count > 0)
				canvas.Children.Remove(canvas.Children.Last());
		}

		public void ChangePenColor(Brush color)
		{
			strokeColor = color;
		}

		public void ChangePenThickness(double value)
		{
			strokeThickness = value;
		}

		private void OnManipulationStarted(object sender, ManipulationStartedRoutedEventArgs e)
		{
			if (e.PointerDeviceType != PointerDeviceType.Touch ||
				PointInPalmBlock(new Point(e.Position.X - palmBlock.Margin.Left, e.Position.Y - palmBlock.Margin.Top)))
				e.Complete();
		}

		private void OnManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
		{
			MovingMove(e.Delta.Translation);
		}

		private void OnManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
		{
			if (actionType == ActionType.Move)
				actionType = ActionType.None;
		}
	}
}
