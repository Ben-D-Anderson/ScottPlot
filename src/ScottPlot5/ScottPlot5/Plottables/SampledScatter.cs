using ScottPlot.DataSources;

namespace ScottPlot.Plottables
{
    public class SampledScatter(IScatterSource data, int sampleSize) : Scatter(data)
    {
        public override void Render(RenderPack rp)
        {
            IReadOnlyList<Coordinates> coordinates = Data.GetScatterPoints();

            //filter coordinates such that we are only considering coordinates
            //in the viewport
            AxisLimits viewport = rp.Plot.Axes.GetLimits(Axes);
            List<Coordinates> coordinatesInViewport = new List<Coordinates>(coordinates.Count);
            coordinatesInViewport.AddRange(coordinates.Where(
                coordinate => coordinate.X >= viewport.Left
                    && coordinate.X <= viewport.Right
                    && coordinate.Y >= viewport.Bottom
                    && coordinate.Y <= viewport.Top));

            //down-sample coordinates in viewport to be no more than desired sample size
            IReadOnlyList<Coordinates> sampledCoordinates =
                new DownSampledListView<Coordinates>(coordinatesInViewport, sampleSize);

            //below is just standard scatter rendering:

            IReadOnlyList<Pixel> markerPixels = sampledCoordinates.SelectView(coordinate =>
                Axes.GetPixel(new(coordinate.X * ScaleX + OffsetX, coordinate.Y * ScaleY + OffsetY)));

            if (markerPixels.Count == 0)
                return;

            IReadOnlyList<Pixel> linePixels = ConnectStyle switch
            {
                ConnectStyle.Straight => markerPixels,
                ConnectStyle.StepHorizontal => GetStepDisplayPixels(markerPixels, true),
                ConnectStyle.StepVertical => GetStepDisplayPixels(markerPixels, false),
                _ => throw new NotImplementedException($"unsupported {nameof(ConnectStyle)}: {ConnectStyle}"),
            };

            using SKPath path = PathStrategy.GetPath(linePixels);

            if (FillY)
            {
                FillStyle fs = new() { IsVisible = true };

                if (ColorPositions.Count > 0)
                {
                    fs.Hatch = Gradient.FromAxisLimits(rp, Data.GetLimits(), AxisGradientDirection, Axes, ColorPositions);
                }

                PixelRect dataPxRect = new(markerPixels);
                PixelRect rect = new(linePixels);
                float yValuePixel = Axes.YAxis.GetPixel(FillYValue + OffsetY, rp.DataRect);

                using SKPath fillPath = new(path);
                fillPath.LineTo(rect.Right, yValuePixel);
                fillPath.LineTo(rect.Left, yValuePixel);

                if (AxisGradientDirection == AxisGradientDirection.Horizontal)
                {
                    bool midWay = yValuePixel < dataPxRect.Bottom && yValuePixel > dataPxRect.Top;
                    bool belowOnly = yValuePixel <= dataPxRect.Top;
                    bool aboveOnly = yValuePixel >= dataPxRect.Bottom;

                    if (midWay || aboveOnly)
                    {
                        PixelRect rectAbove = new(rp.DataRect.Left, rp.DataRect.Right, yValuePixel, rect.Top);
                        rp.CanvasState.Save();
                        rp.CanvasState.Clip(rectAbove);
                        fs.Color = ColorPositions.Count > 0 ? Colors.Black : FillYAboveColor;
                        Drawing.FillPath(rp.Canvas, rp.Paint, fillPath, fs, rectAbove);
                        rp.CanvasState.Restore();
                    }

                    if (midWay || belowOnly)
                    {
                        PixelRect rectBelow = new(rp.DataRect.Left, rp.DataRect.Right, rect.Bottom, yValuePixel);
                        rp.CanvasState.Save();
                        rp.CanvasState.Clip(rectBelow);
                        fs.Color = ColorPositions.Count > 0 ? Colors.Black : FillYBelowColor;
                        Drawing.FillPath(rp.Canvas, rp.Paint, fillPath, fs, rectBelow);
                        rp.CanvasState.Restore();
                    }
                }
                else if (AxisGradientDirection == AxisGradientDirection.Vertical)
                {
                    PixelRect fullRect = new(rp.DataRect.Left, rp.DataRect.Right, rect.Bottom, rect.Top);
                    rp.CanvasState.Save();
                    rp.CanvasState.Clip(fullRect);
                    fs.Color = ColorPositions.Count > 0 ? Colors.Black : FillYColor;
                    Drawing.FillPath(rp.Canvas, rp.Paint, fillPath, fs, fullRect);
                    rp.CanvasState.Restore();
                }
            }

            Drawing.DrawLines(rp.Canvas, rp.Paint, path, LineStyle);
            Drawing.DrawMarkers(rp.Canvas, rp.Paint, markerPixels, MarkerStyle);
        }

        private class DownSampledListView<T> : IReadOnlyList<T>
        {
            private readonly IReadOnlyList<T> _inputList;
            private readonly int _desiredCount;

            public DownSampledListView(IReadOnlyList<T> inputList, int desiredCount)
            {
                _inputList = inputList ?? throw new ArgumentNullException(nameof(inputList));
                _desiredCount = desiredCount > 0 ? desiredCount : throw new ArgumentOutOfRangeException(nameof(desiredCount));
            }

            public T this[int index]
            {
                get
                {
                    int visibleCount = Count;
                    if (index < 0 || index >= visibleCount)
                        throw new ArgumentOutOfRangeException(nameof(index));

                    if (visibleCount < _desiredCount)
                        //list is smaller than sample size so return list item
                        return _inputList[index];

                    int inputCount = _inputList.Count;
                    int step = (int)Math.Round((double)(inputCount - 1) / (visibleCount - 1));
                    int innerIndex = Math.Min(index * step, inputCount - 1);
                    return _inputList[innerIndex];
                }
            }

            public int Count => Math.Min(_desiredCount, _inputList.Count);

            public IEnumerator<T> GetEnumerator()
            {
                for (int i = 0; i < Count; i++)
                {
                    yield return this[i];
                }
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }
    }
}
