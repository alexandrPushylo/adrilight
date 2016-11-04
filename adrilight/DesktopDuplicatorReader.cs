﻿

using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading;
using System.Threading.Tasks;
using adrilight.DesktopDuplication;
using Polly;

namespace adrilight
{
  public class DesktopDuplicatorReader
    {
        public bool IsRunning { get; private set; } = false;

      private readonly Policy _retryPolicy = Policy.Handle<Exception>().
            WaitAndRetry(13, index => index < 3 ? TimeSpan.FromMilliseconds(100) : TimeSpan.FromSeconds(3));

      private DesktopDuplicator _desktopDuplicator;

      public void Run(CancellationToken token)
        {
            if(IsRunning) throw new Exception(nameof(DesktopDuplicatorReader) +" is already running!");

            IsRunning = true;
                Debug.WriteLine("Started Desktop Duplication Reader.");
            try
            {
                BitmapData bitmapData = new BitmapData();

                while (!token.IsCancellationRequested)
                {

                    var frame = _retryPolicy.Execute<DesktopFrame>(GetNextFrame);
                    if (frame == null)
                    {
                        continue;
                    }

                    var image = frame.DesktopImage;
                     image.LockBits(new Rectangle(0, 0, image.Width, image.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb, bitmapData);
                    lock (SpotSet.Lock)
                    {

                        Parallel.ForEach(SpotSet.Spots, spot =>
                        {

                            const int numberOfSteps = 15;
                            int stepx = Math.Max(1, spot.Width/numberOfSteps);
                            int stepy = Math.Max(1, spot.Height/numberOfSteps);

                            int sumR;
                            int sumG;
                            int sumB;
                            int count;
                            GetAverageColorOfRectangularRegion(spot.Rectangle, stepy, stepx, bitmapData, out sumR, out sumG, out sumB, out count);
                            //                            White balance    1,0000  0,8730  0,7453

                            if (sumR <= Settings.SaturationTreshold && sumG <= Settings.SaturationTreshold && sumB <= Settings.SaturationTreshold)
                            {
                                spot.SetColor(Color.Black);
                            }
                            else
                            {
                                spot.SetColor((byte) (sumR*1.0f/count), (byte) (sumG*0.8730f/count), (byte) (sumB*0.7453f/count));
                            }
                        });
                    }
                    image.UnlockBits(bitmapData);
                }
            }
            finally
            {
                _desktopDuplicator?.Dispose();
                Debug.WriteLine("Stopped Desktop Duplication Reader.");
                IsRunning = false;
            }
        }

      private DesktopFrame GetNextFrame()
      {
          if (_desktopDuplicator == null)
          {
              _desktopDuplicator = new DesktopDuplicator(0);
          }

          try
          {
              var frame = _desktopDuplicator.GetLatestFrame();
              return frame;
          }
          catch (Exception)
          {
              _desktopDuplicator?.Dispose();
              _desktopDuplicator = null;
              throw;
          }
      }

      public static unsafe void GetAverageColorOfRectangularRegion(Rectangle spotRectangle, int stepy, int stepx, BitmapData bitmapData, out int sumR, out int sumG, out int sumB, out int count)
        {
            sumR = 0;
            sumG = 0;
            sumB = 0;
            count = 0;

            var stepCount = spotRectangle.Width / stepx;
            var stepxTimes4 = stepx * 4;
            for (var y = spotRectangle.Top; y < spotRectangle.Bottom; y += stepy)
            {
                byte* pointer = (byte*)bitmapData.Scan0 + bitmapData.Stride * y + 4 * spotRectangle.Left;
                for (int i = 0; i < stepCount; i++)
                {
                    sumR += pointer[2];
                    sumG += pointer[1];
                    sumB += pointer[0];

                    pointer += stepxTimes4;
                }
                count += stepCount;
            }
        }

    }
}
