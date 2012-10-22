using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI;
using Windows.UI.Input;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using Windows.UI.Xaml.Shapes;

// 사용자 정의 컨트롤 항목 템플릿에 대한 설명은 http://go.microsoft.com/fwlink/?LinkId=234236에 나와 있습니다.

namespace PenTouch
{
	public sealed partial class CanvasEx : Canvas
	{
		public CanvasEx()
		{
			this.InitializeComponent();
		}

		private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
		{
			var point = e.GetCurrentPoint(this);
			var pointerPoint = PointerPoint.GetCurrentPoint(e.Pointer.PointerId);

			Debug.WriteLine(e.GetCurrentPoint(this).Position.ToString() + "(" + pointerPoint.Properties.Pressure + ")" + " : " + e.Pointer.PointerDeviceType + e.Pointer.PointerId);

			Ellipse ellipse = new Ellipse();
			float pressure = pointerPoint.Properties.Pressure * 30;

			ellipse.Width = pressure;
			ellipse.Height = pressure;
			ellipse.Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(0xff, 0xff, 0xff, 0x00));
			ellipse.Margin = new Thickness(point.Position.X - pressure / 2, point.Position.Y - pressure / 2, 0, 0);

			Children.Add(ellipse);
		}
	}
}
