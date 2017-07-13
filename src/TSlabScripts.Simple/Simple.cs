using System;
using System.Collections.Generic;
using System.Linq;
using TSLab.DataSource;
using TSLab.Script;
using TSLab.Script.Handlers;
using TSLab.Script.Optimization;

namespace TSlabScripts.Simple
{
    public class Simple : IExternalScript
    {
        public OptimProperty Slippage = new OptimProperty(30, 0, 100, 10);
        public OptimProperty Value = new OptimProperty(1, 0, 100, 1);
        public OptimProperty LengthSegmentAB = new OptimProperty(1000, double.MinValue, double.MaxValue, 0.01);
        public OptimProperty LengthSegmentBC = new OptimProperty(390, double.MinValue, double.MaxValue, 0.01);
        public OptimProperty ScopeDelta = new OptimProperty(50, double.MinValue, double.MaxValue, 0.01);
        public OptimProperty ScopeProfite = new OptimProperty(100, double.MinValue, double.MaxValue, 0.01);
        public OptimProperty ScopeStope = new OptimProperty(300, double.MinValue, double.MaxValue, 0.01);

        public TimeSpan TimeCloseAllPosition = new TimeSpan(18, 40, 00);
        public TimeSpan TimeBeginDayBar = new TimeSpan(10, 00, 00);
        public TimeSpan TimeBeginBar = new TimeSpan(10, 04, 55);

        private static IContext TSLabContext { get; set; }
        private static ISecurity TSLabSource { get; set; }
        private static ISecurity TSLabCompressSource { get; set; }
        public static List<double> BuySignal { get; set; }
        public static List<double> SellSignal { get; set; }

        public virtual void Execute(IContext ctx, ISecurity source)
        {
            TSLabContext = ctx;
            TSLabSource = source;

            // Проверяем таймфрейм входных данных
            if (!GetValidTimeFrame(TSLabSource.IntervalBase, TSLabSource.Interval)) return;

            // Компрессия исходного таймфрейма в пятиминутный
            TSLabCompressSource = TSLabSource.CompressTo(new Interval(5, DataIntervals.MINUTE), 0, 200, 0);

            // Генерация графика исходного таймфрейма
            var pain = TSLabContext.CreatePane("Original", 70, false);
            pain.AddList(source.Symbol, TSLabCompressSource, CandleStyles.BAR_CANDLE, new Color(100, 100, 100), PaneSides.RIGHT);
            pain.AddList(source.Symbol, TSLabSource, CandleStyles.BAR_CANDLE, new Color(0, 0, 0), PaneSides.RIGHT);


            BuySignal = new List<double>();
            SellSignal = new List<double>();
            for (int i = 0; i < TSLabSource.Bars.Count; i++)
            {
                BuySignal.Add(0);
                SellSignal.Add(0);
            }

            for (var historyBar = 1; historyBar <= TSLabSource.Bars.Count - 1; historyBar++)
            {
                Trading(historyBar);
            }

            var buyPain = ctx.CreatePane("BuySignal", 15, false);
            buyPain.AddList("BuySignal", BuySignal, ListStyles.HISTOHRAM_FILL, new Color(0, 255, 0), LineStyles.SOLID,
                PaneSides.RIGHT);

            var sellPain = ctx.CreatePane("SellSignal", 15, false);
            sellPain.AddList("SellSignal", SellSignal, ListStyles.HISTOHRAM_FILL, new Color(255, 0, 0), LineStyles.SOLID,
                PaneSides.RIGHT);
        }

        private void Trading(int actualBar)
        {
            if (TSLabSource.Bars[actualBar].Date.TimeOfDay < TimeBeginBar) return;

            // Если время 18:40 или более - закрыть все активные позиции и не торговать
            if (TSLabSource.Bars[actualBar].Date.TimeOfDay >= TimeCloseAllPosition)
            {
                if (TSLabSource.Positions.ActivePositionCount > 0)
                {
                    CloseAllPosition(actualBar);
                }
                return;
            }

            // Посик активных позиций
            if (TSLabSource.Positions.ActivePositionCount > 0)
            {
                SetStopForActivePosition(actualBar);
            }

            var totalSecondsActualBar = TSLabSource.Bars[actualBar].Date.TimeOfDay.TotalSeconds;

            if ((totalSecondsActualBar + 5) % 300 == 0)
            {
                var dateActualBar = TSLabSource.Bars[actualBar].Date;

                var indexBeginDayBar = TSLabCompressSource.Bars
                    .Select((bar, index) => new { Index = index, Bar = bar })
                    .Last(item =>
                    item.Bar.Date.TimeOfDay == TimeBeginDayBar &&
                    item.Bar.Date.Day == dateActualBar.Day &&
                    item.Bar.Date.Month == dateActualBar.Month &&
                    item.Bar.Date.Year == dateActualBar.Year).Index;

                int indexCompressBar = GetIndexActualCompressBar(dateActualBar, indexBeginDayBar);

                // Поиск моделей на покупку
                SearchBuyModel(indexCompressBar, indexBeginDayBar, actualBar);

                // Поиск моделей на продажу и выставление для них ордеров
                SearchSellModel(indexCompressBar, indexBeginDayBar, actualBar);
            }

            var modelBuyList = (List<double>)TSLabContext.LoadObject("BuyModel") ?? new List<double>();
            if (modelBuyList.Any())
            {
                var buyList = ValidateBuyModel(modelBuyList, actualBar);
                foreach (double value in buyList)
                {
                    TSLabSource.Positions.BuyIfGreater(actualBar + 1, Value, value - ScopeDelta, Slippage, "buy_" + value);
                }
                TSLabContext.StoreObject("BuyModel", buyList);
            }

            var modelSellList = (List<double>)TSLabContext.LoadObject("SellModel") ?? new List<double>();
            if (modelSellList.Any())
            {
                var sellList = ValidateSellModel(modelSellList, actualBar);
                foreach (double value in sellList)
                {
                    TSLabSource.Positions.SellIfLess(actualBar + 1, Value, value + ScopeDelta, Slippage, "sell_" + value);
                }
                TSLabContext.StoreObject("SellList", sellList);
            }
        }

        private void SearchBuyModel(int indexCompressBar, int indexBeginDayBar, int actualBar)
        {
            var modelBuyList = new List<double>();

            for (var indexPointA = indexCompressBar - 1; indexPointA >= indexBeginDayBar && indexPointA >= 0; indexPointA--)
            {
                var pointB = GetHighPrices(TSLabCompressSource.HighPrices, indexPointA, indexCompressBar);
                var realPointA = GetLowPrices(TSLabCompressSource.LowPrices, indexPointA, pointB.Index);

                // Точки A и B не могут быть на одном баре
                if (pointB.Index == realPointA.Index) continue;

                // Проверм размер фигуры A-B
                var ab = pointB.Value - realPointA.Value;
                if (ab <= LengthSegmentBC || ab >= LengthSegmentAB) continue;

                var pointC = GetLowPrices(TSLabCompressSource.LowPrices, pointB.Index, indexCompressBar);

                // Точки B и C не могут быть на одном баре
                if (pointB.Index == pointC.Index) continue;

                // Проверям размер модели B-C
                if (pointB.Value - pointC.Value <= LengthSegmentBC ||
                    pointC.Value - realPointA.Value < 0) continue;

                // Проверка на пересечение
                if (indexCompressBar != pointC.Index)
                {
                    var validateMax = TSLabCompressSource.HighPrices.
                        Skip(pointC.Index + 1).
                        Take(indexCompressBar - pointC.Index).
                        Max();
                    if (pointB.Value - ScopeDelta <= validateMax) continue;
                }

                modelBuyList.Add(pointB.Value);

                BuySignal[actualBar] = 1;
            }

            TSLabContext.StoreObject("BuyModel", modelBuyList);
        }

        private void SearchSellModel(int indexCompressBar, int indexBeginDayBar, int actualBar)
        {
            var modelSellList = new List<double>();

            for (var indexPointA = indexCompressBar - 1; indexPointA >= indexBeginDayBar && indexPointA >= 0; indexPointA--)
            {
                var pointB = TSLabCompressSource.LowPrices.
                    Select((value, index) => new { Value = value, Index = index }).
                    Skip(indexPointA).
                    Take(indexCompressBar - indexPointA + 1).
                    OrderBy(x => x.Value).First();

                var realPointA = TSLabCompressSource.HighPrices.
                    Select((value, index) => new { Value = value, Index = index }).
                    Skip(indexPointA).
                    Take(pointB.Index - indexPointA + 1).
                    OrderBy(x => x.Value).Last();

                // Точки A и B не могут быть на одном баре
                if (pointB.Index == realPointA.Index) continue;

                // Проверм размер фигуры A-B
                var ab = realPointA.Value - pointB.Value;
                if (ab <= LengthSegmentBC || ab >= LengthSegmentAB) continue;

                var pointC = TSLabCompressSource.HighPrices.
                    Select((value, index) => new { Value = value, Index = index }).
                    Skip(pointB.Index).
                    Take(indexCompressBar - pointB.Index + 1).
                    OrderBy(x => x.Value).Last();

                // Точки B и C не могут быть на одном баре
                if (pointB.Index == pointC.Index) continue;

                // Проверям размер модели B-C
                if (pointC.Value - pointB.Value <= LengthSegmentBC ||
                    realPointA.Value - pointC.Value < 0) continue;

                // Проверка на пересечение
                if (indexCompressBar != pointC.Index)
                {
                    var validateMin = TSLabCompressSource.LowPrices.
                        Skip(pointC.Index + 1).
                        Take(indexCompressBar - pointC.Index).
                        Min();
                    if (pointB.Value + ScopeDelta >= validateMin) continue;
                }

                modelSellList.Add(pointB.Value);

                SellSignal[actualBar] = 1;
            }

            TSLabContext.StoreObject("SellModel", modelSellList);
        }

        private Point GetLowPrices(IList<double> collection, int leftSide, int rigthSide)
        {
            return collection.Select((value, index) => new Point { Value = value, Index = index }).
                    Skip(leftSide).
                    Take(rigthSide - leftSide + 1).
                    OrderBy(x => x.Value).ThenByDescending(x => x.Index).First();
        }

        private Point GetHighPrices(IList<double> collection, int leftSide, int rigthSide)
        {
            return collection.Select((value, index) => new Point { Value = value, Index = index }).
                    Skip(leftSide).
                    Take(rigthSide - rigthSide + 1).
                    OrderBy(x => x.Value).ThenBy(x => x.Index).Last(); ;
        }

        private List<double> ValidateBuyModel(List<double> modelBuyList, int actualBar)
        {
            double lastMax = double.MinValue;

            for (var i = actualBar; i >= 0 && (TSLabSource.Bars[i].Date.TimeOfDay.TotalSeconds + 5) % 300 != 0; i--)
            {
                lastMax = TSLabSource.HighPrices[i] > lastMax ? TSLabSource.HighPrices[i] : lastMax;
            }

            return modelBuyList.Where(value => value - ScopeDelta > lastMax).ToList();
        }

        private List<double> ValidateSellModel(List<double> modelSellList, int actualBar)
        {
            double lastMin = double.MaxValue;

            for (var i = actualBar; i >= 0 && (TSLabSource.Bars[i].Date.TimeOfDay.TotalSeconds + 5) % 300 != 0; i--)
            {
                lastMin = TSLabSource.LowPrices[i] < lastMin ? TSLabSource.LowPrices[i] : lastMin;
            }

            return modelSellList.Where(value => value + ScopeDelta < lastMin).ToList();
        }

        private void SetStopForActivePosition(int actualBar)
        {
            var positionList = TSLabSource.Positions.GetActiveForBar(actualBar);

            foreach (var position in positionList)
            {
                var arr = position.EntrySignalName.Split('_');
                switch (arr[0])
                {
                    case "buy":
                        position.CloseAtProfit(actualBar + 1, Convert.ToDouble(arr[1]) + ScopeProfite, "closeProfit");
                        position.CloseAtStop(actualBar + 1, Convert.ToDouble(arr[1]) - ScopeStope, Slippage, "closeStop");
                        break;
                    case "sell":
                        position.CloseAtProfit(actualBar + 1, Convert.ToDouble(arr[1]) - ScopeProfite, "closeProfit");
                        position.CloseAtStop(actualBar + 1, Convert.ToDouble(arr[1]) + ScopeStope, Slippage, "closeStop");
                        break;
                }
            }
        }

        private void CloseAllPosition(int actualBar)
        {
            var positionList = TSLabSource.Positions;

            foreach (var position in positionList)
            {
                if (position.IsActive)
                {
                    position.CloseAtMarket(actualBar + 1, "closeAtTime");
                }
            }
        }

        public static bool GetValidTimeFrame(DataIntervals intervalBase, int interval)
        {
            if (intervalBase == DataIntervals.SECONDS && interval == 5)
            {
                return true;
            }

            TSLabContext.Log("Выбран не верный таймфрейм, выберите таймфрейм равный 5 секундам", new Color(255, 0, 0), true);
            return false;
        }

        public static int GetIndexActualCompressBar(DateTime dateActualBar, int indexBeginDayBar)
        {
            return indexBeginDayBar + (int)((dateActualBar.TimeOfDay.TotalMinutes - 600) / 5);
        }
    }

    public class Point
    {
        public int Index { get; set; }

        public double Value { get; set; }
    }
}
