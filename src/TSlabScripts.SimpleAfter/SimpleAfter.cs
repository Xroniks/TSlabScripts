using System;
using System.Collections.Generic;
using System.Linq;
using TSlabScripts.Common;
using TSLab.DataSource;
using TSLab.Script;
using TSLab.Script.Handlers;
using TSLab.Script.Optimization;

namespace TSlabScripts.SimpleAfter
{
    public class SimpleAfter : IExternalScript
    {
        public OptimProperty Slippage = new OptimProperty(30, 0, 100, 10);
        public OptimProperty Shares = new OptimProperty(1, 0, 100, 1);
        public OptimProperty LengthSegmentAB = new OptimProperty(1000, 0, double.MaxValue, 10);
        public OptimProperty LengthSegmentBC = new OptimProperty(390, 0, double.MaxValue, 10);
        public OptimProperty ScopeDelta = new OptimProperty(50, 0, double.MaxValue, 10);
        public OptimProperty ScopeProfite = new OptimProperty(100, 0, double.MaxValue, 10);
        public OptimProperty ScopeStope = new OptimProperty(300, 0, double.MaxValue, 10);
        public OptimProperty HasAfter = new OptimProperty(0, 0, 1, 1);
        public OptimProperty HasAfterCloseBar = new OptimProperty(0, 0, 1, 1);

        public OptimProperty WaitAfterClosePosition = new OptimProperty(50, 1, 1000, 1);
        public OptimProperty ScopeProfiteAfter = new OptimProperty(200, 0, double.MaxValue, 10);
        public OptimProperty ScopeStopAfter = new OptimProperty(250, 0, double.MaxValue, 10);

        private readonly TimeSpan TimeCloseAllPosition = new TimeSpan(18, 40, 00);
        private readonly TimeSpan TimeBeginBar = new TimeSpan(10, 04, 55);

        private static IContext TsLabContext { get; set; }
        private static ISecurity TsLabSource { get; set; }
        private static ISecurity TsLabCompressSource { get; set; }
        private ModelIndicator ModelIndicator { get; set; }

        public virtual void Execute(IContext ctx, ISecurity source)
        {
            TsLabContext = ctx;
            TsLabSource = source;

            // Проверяем таймфрейм входных данных
            if (!SimpleService.GetValidTimeFrame(TsLabSource.IntervalBase, TsLabSource.Interval))
            {
                TsLabContext.Log("Выбран не верный таймфрейм, выберите таймфрейм равный 5 секундам", new Color(255, 0, 0), true);
                return;
            }

            // Компрессия исходного таймфрейма в пятиминутный
            TsLabCompressSource = TsLabSource.CompressTo(new Interval(5, DataIntervals.MINUTE), 0, 200, 0);

            SimpleHealper.RenderBars(TsLabContext, TsLabSource, TsLabCompressSource);
            ModelIndicator = new ModelIndicator(TsLabSource.Bars.Count);

            for (var historyBar = 1; historyBar <= TsLabSource.Bars.Count - 1; historyBar++)
            {
                Trading(historyBar);
            }

            SimpleHealper.RenderModelIndicator(TsLabContext, ModelIndicator);
        }

        private void Trading(int actualBar)
        {
            // Если время менее 10:00 - не торговать
            if (TsLabSource.Bars[actualBar].Date.TimeOfDay < TimeBeginBar) return;

            // Если время 18:40 или более - закрыть все активные позиции и не торговать
            if (TsLabSource.Bars[actualBar].Date.TimeOfDay >= TimeCloseAllPosition)
            {
                if (TsLabSource.Positions.ActivePositionCount > 0)
                {
                    SimpleHealper.CloseAllPosition(TsLabSource, actualBar);
                }
                return;
            }

            SetStopForActivePosition(actualBar);

            if (SimpleService.IsStartFiveMinutesBar(TsLabSource, actualBar))
            {
                var dateActualBar = TsLabSource.Bars[actualBar].Date;
                var indexBeginDayBar = SimpleService.GetIndexBeginDayBar(TsLabCompressSource, dateActualBar);
                var indexCompressBar = SimpleService.GetIndexActualCompressBar(TsLabCompressSource, dateActualBar, indexBeginDayBar);

                SearchBuyModel(indexCompressBar, indexBeginDayBar, actualBar);
                SearchSellModel(indexCompressBar, indexBeginDayBar, actualBar);
            }

            if (HasAfterCloseBar == 1)
            {
                SetStopToOpenAfterCloseBar(actualBar);
            }

            SetStopToOpenPosition(actualBar);
        }

        private void SetStopToOpenAfterCloseBar(int actualBar)
        {
            var tempTime = new TimeSpan(0, WaitAfterClosePosition, 0);
            var positions = TsLabSource.Positions
                .Where(x => !x.IsActive 
                    && x.Profit() < 0 
                    && TsLabSource.Bars[actualBar].Date - x.ExitBar.Date <= tempTime
                    && (x.EntrySignalName.Split('_')[0] == "sell" || x.EntrySignalName.Split('_')[0] == "buy")).ToList();

            foreach (var position in positions)
            {
                var startDate = new DateTime(
                        position.ExitBar.Date.Year,
                        position.ExitBar.Date.Month,
                        position.ExitBar.Date.Day,
                        position.ExitBar.Date.Hour,
                        Convert.ToInt32(Math.Floor(Convert.ToDouble(position.ExitBar.Date.Minute / 5)))*5,
                        0);
                var endDate = startDate.AddMinutes(4).AddSeconds(55);

                var fistBarAfterEntry = TsLabSource.Bars.FirstOrDefault(x => x.Date > endDate);
                if (fistBarAfterEntry == null) continue;
                var indexFistBarAfterEntry = TsLabSource.Bars.IndexOf(fistBarAfterEntry);
                if (indexFistBarAfterEntry > actualBar) continue;

                if (position.IsLong)
                {
                    var valueOpen = TsLabSource.Bars.Where(x => x.Date >= startDate && x.Date <= endDate).Min(x => x.Low);
                    if (TsLabSource.LowPrices.Skip(indexFistBarAfterEntry).Take(actualBar - indexFistBarAfterEntry + 1).Any(x => x < valueOpen)) continue;
                    TsLabSource.Positions.SellIfLess(actualBar + 1, Shares, valueOpen, Slippage,"sellAfter_" + valueOpen);
                }
                else
                {
                    var valueOpen = TsLabSource.Bars.Where(x => x.Date >= startDate && x.Date <= endDate).Max(x => x.High);
                    if (TsLabSource.HighPrices.Skip(indexFistBarAfterEntry).Take(actualBar - indexFistBarAfterEntry + 1).Any(x => x > valueOpen)) continue;
                    TsLabSource.Positions.BuyIfGreater(actualBar + 1, Shares, valueOpen, "buyAfter_" + valueOpen);
                }
            }
        }

        private void SetStopToOpenPosition(int actualBar)
        {
            var modelBuyList = (List<double>)TsLabContext.LoadObject("BuyModel") ?? new List<double>();
            if (modelBuyList.Any())
            {
                var buyList = ValidateBuyModel(modelBuyList, actualBar);
                foreach (double value in buyList)
                {
                    TsLabSource.Positions.BuyIfGreater(actualBar + 1, Shares, value - ScopeDelta, Slippage, "buy_" + value);
                }
                TsLabContext.StoreObject("BuyModel", buyList);
            }

            var modelSellList = (List<double>)TsLabContext.LoadObject("SellModel") ?? new List<double>();
            if (modelSellList.Any())
            {
                var sellList = ValidateSellModel(modelSellList, actualBar);
                foreach (double value in sellList)
                {
                    TsLabSource.Positions.SellIfLess(actualBar + 1, Shares, value + ScopeDelta, Slippage, "sell_" + value);
                }
                TsLabContext.StoreObject("SellList", sellList);
            }
        }

        private void SearchBuyModel(int indexCompressBar, int indexBeginDayBar, int actualBar)
        {
            var modelBuyList = new List<double>();

            for (var pointA = new Point { Index = indexCompressBar - 1 };
                pointA.Index >= indexBeginDayBar && pointA.Index >= 0;
                pointA.Index--)
            {
                var pointC = SimpleService.GetLowPrices(TsLabCompressSource, pointA.Index, indexCompressBar, false, true);
                var pointB = SimpleService.GetHighPrices(TsLabCompressSource, pointA.Index, pointC.Index, true, true);
                pointA.Low = TsLabCompressSource.LowPrices[pointA.Index];

                if (pointB.Index == pointA.Index) continue;
                if (pointB.Index == pointC.Index) continue;

                // Проверм размер фигуры A-B
                var ab = pointB.High - pointA.Low;
                if (ab <= LengthSegmentBC || ab >= LengthSegmentAB) continue;

                // Проверям размер модели B-C
                if (pointB.High - pointC.Low <= LengthSegmentBC ||
                    pointC.Low - pointA.Low < 0) continue;

                // Проверка на пересечение
                if (indexCompressBar != pointC.Index)
                {
                    var validateMax = TsLabCompressSource.HighPrices.
                        Skip(pointC.Index + 1).
                        Take(indexCompressBar - pointC.Index).
                        Max();
                    if (pointB.High - ScopeDelta <= validateMax) continue;
                }

                modelBuyList.Add(pointB.High);

                ModelIndicator.BuySignal[actualBar] = 1;
            }

            TsLabContext.StoreObject("BuyModel", modelBuyList);
        }

        private void SearchSellModel(int indexCompressBar, int indexBeginDayBar, int actualBar)
        {
            var modelSellList = new List<double>();

            for (var pointA = new Point { Index = indexCompressBar - 1 };
                pointA.Index >= indexBeginDayBar && pointA.Index >= 0;
                pointA.Index--)
            {
                var pointC = SimpleService.GetHighPrices(TsLabCompressSource, pointA.Index, indexCompressBar, false, true);
                var pointB = SimpleService.GetLowPrices(TsLabCompressSource, pointA.Index, pointC.Index, true, true);
                pointA.High = TsLabCompressSource.HighPrices[pointA.Index];

                if (pointB.Index == pointA.Index) continue;
                if (pointB.Index == pointC.Index) continue;

                // Проверм размер фигуры A-B
                var ab = pointA.High - pointB.Low;
                if (ab <= LengthSegmentBC || ab >= LengthSegmentAB) continue;

                // Проверям размер модели B-C
                if (pointC.High - pointB.Low <= LengthSegmentBC ||
                    pointA.High - pointC.High < 0) continue;

                // Проверка на пересечение
                if (indexCompressBar != pointC.Index)
                {
                    var validateMin = TsLabCompressSource.LowPrices.
                        Skip(pointC.Index + 1).
                        Take(indexCompressBar - pointC.Index).
                        Min();
                    if (pointB.Low + ScopeDelta >= validateMin) continue;
                }

                modelSellList.Add(pointB.Low);

                ModelIndicator.SellSignal[actualBar] = 1;
            }

            TsLabContext.StoreObject("SellModel", modelSellList);
        }

        private List<double> ValidateBuyModel(List<double> modelBuyList, int actualBar)
        {
            double lastMax = double.MinValue;

            for (var i = actualBar; i >= 0 && !SimpleService.IsStartFiveMinutesBar(TsLabSource, i); i--)
            {
                lastMax = TsLabSource.HighPrices[i] > lastMax ? TsLabSource.HighPrices[i] : lastMax;
            }

            return modelBuyList.Where(value => value - ScopeDelta > lastMax).ToList();
        }

        private List<double> ValidateSellModel(List<double> modelSellList, int actualBar)
        {
            double lastMin = double.MaxValue;

            for (var i = actualBar; i >= 0 && !SimpleService.IsStartFiveMinutesBar(TsLabSource, i); i--)
            {
                lastMin = TsLabSource.LowPrices[i] < lastMin ? TsLabSource.LowPrices[i] : lastMin;
            }

            return modelSellList.Where(value => value + ScopeDelta < lastMin).ToList();
        }

        private void SetStopForActivePosition(int actualBar)
        {
            if (TsLabSource.Positions.ActivePositionCount <= 0)
            {
                return;
            }

            var positionList = TsLabSource.Positions.GetActiveForBar(actualBar);

            foreach (var position in positionList)
            {
                var arr = position.EntrySignalName.Split('_');
                switch (arr[0])
                {
                    case "buy":
                        position.CloseAtProfit(actualBar + 1, Convert.ToDouble(arr[1]) + ScopeProfite, "buyCloseProfit");
                        position.CloseAtStop(actualBar + 1, Convert.ToDouble(arr[1]) - ScopeStope, Slippage, "buyCloseStop");
                        if (HasAfter == 1)
                        {
                            TsLabSource.Positions.SellIfLess(actualBar + 1, Shares,
                                Convert.ToDouble(arr[1]) - ScopeStope, Slippage,
                                $"sellAfter_{Convert.ToDouble(arr[1]) - ScopeStope}");
                        }
                        break;
                    case "sell":
                        position.CloseAtProfit(actualBar + 1, Convert.ToDouble(arr[1]) - ScopeProfite, "sellCloseProfit");
                        position.CloseAtStop(actualBar + 1, Convert.ToDouble(arr[1]) + ScopeStope, Slippage, "sellCloseStop");
                        if (HasAfter == 1)
                        {
                            TsLabSource.Positions.BuyIfGreater(actualBar + 1, Shares,
                                Convert.ToDouble(arr[1]) + ScopeStope, Slippage,
                                $"buyAfter_{Convert.ToDouble(arr[1]) + ScopeStope}");
                        }
                        break;
                    case "sellAfter":
                        position.CloseAtProfit(actualBar + 1, Convert.ToDouble(arr[1]) - ScopeProfiteAfter, "sellAfterCloseProfit");
                        position.CloseAtStop(actualBar + 1, Convert.ToDouble(arr[1]) + ScopeStopAfter, Slippage, "sellAfterCloseStop");
                        break;
                    case "buyAfter":
                        position.CloseAtProfit(actualBar + 1, Convert.ToDouble(arr[1]) + ScopeProfiteAfter, "buyAfterCloseProfit");
                        position.CloseAtStop(actualBar + 1, Convert.ToDouble(arr[1]) - ScopeStopAfter, Slippage, "buyAfterCloseStop");
                        break;
                }
            }
        }
    }
}
