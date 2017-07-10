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
        public TimeSpan FiveSeconds = new TimeSpan(0, 0, 5);
        public TimeSpan FiveMinutes = new TimeSpan(0, 5, 0);

        public virtual void Execute(IContext ctx, ISecurity source)
        {
            // Проверяем таймфрейм входных данных
            if (!GetValidTimeFrame(ctx, source)) return;

            // Компрессия исходного таймфрейма в пятиминутный
            var compressSource = source.CompressTo(new Interval(5, DataIntervals.MINUTE), 0, 200, 0);

            // Генерация графика исходного таймфрейма
            var pain = ctx.CreatePane("Original", 70, false);
            pain.AddList(source.Symbol, compressSource, CandleStyles.BAR_CANDLE, new Color(100, 100, 100), PaneSides.RIGHT);
            pain.AddList(source.Symbol, source, CandleStyles.BAR_CANDLE, new Color(0, 0, 0), PaneSides.RIGHT);


            var buySignal = new List<double>();
            var sellSignal = new List<double>();
            for (int i = 0; i < source.Bars.Count; i++)
            {
                buySignal.Add(0);
                sellSignal.Add(0);
            }

            for (var historyBar = 1; historyBar <= source.Bars.Count - 1; historyBar++)
            {
                Trading(ctx, source, compressSource, historyBar, buySignal, sellSignal);
            }

            var buyPain = ctx.CreatePane("BuySignal", 15, false);
            buyPain.AddList("BuySignal", buySignal, ListStyles.HISTOHRAM_FILL, new Color(0, 255, 0), LineStyles.SOLID,
                PaneSides.RIGHT);

            var sellPain = ctx.CreatePane("SellSignal", 15, false);
            sellPain.AddList("SellSignal", sellSignal, ListStyles.HISTOHRAM_FILL, new Color(255, 0, 0), LineStyles.SOLID,
                PaneSides.RIGHT);
        }

        public void Trading(IContext ctx, ISecurity source, ISecurity compressSource, int actualBar, List<double> buySignal, List<double> sellSignal)
        {
            if (source.Bars[actualBar].Date.TimeOfDay < TimeBeginBar) return;

            // Если время 18:40 или более - закрыть все активные позиции и не торговать
            if (source.Bars[actualBar].Date.TimeOfDay >= TimeCloseAllPosition)
            {
                if (source.Positions.ActivePositionCount > 0)
                {
                    CloseAllPosition(source, actualBar);
                }
                return;
            }

            // Посик активных позиций
            if (source.Positions.ActivePositionCount > 0)
            {
                SearchActivePosition(source, actualBar);
            }

            var totalSecondsActualBar = source.Bars[actualBar].Date.TimeOfDay.TotalSeconds;

            if ((totalSecondsActualBar + 5) % 300 == 0)
            {
                var dateActualBar = source.Bars[actualBar].Date;

                var indexBeginDayBar = compressSource.Bars
                    .Select((bar, index) => new { Index = index, Bar = bar })
                    .Last(item =>
                    item.Bar.Date.TimeOfDay == TimeBeginDayBar &&
                    item.Bar.Date.Day == dateActualBar.Day &&
                    item.Bar.Date.Month == dateActualBar.Month &&
                    item.Bar.Date.Year == dateActualBar.Year).Index;

                int indexCompressBar = GetIndexCompressBar(compressSource, dateActualBar, indexBeginDayBar);

                // Поиск моделей на покупку
                SearchBuyModel(ctx, compressSource, indexCompressBar, indexBeginDayBar, actualBar, buySignal);

                // Поиск моделей на продажу и выставление для них ордеров
                SearchSellModel(ctx, compressSource, indexCompressBar, indexBeginDayBar, actualBar, sellSignal);
            }

            var modelBuyList = (List<double>)ctx.LoadObject("BuyModel") ?? new List<double>();
            if (modelBuyList.Any())
            {
                var buyList = ValidateBuyModel(source, modelBuyList, actualBar);
                foreach (double value in buyList)
                {
                    source.Positions.BuyIfGreater(actualBar + 1, Value, value - ScopeDelta, Slippage, "buy_" + value);
                }
                ctx.StoreObject("BuyModel", buyList);
            }

            var modelSellList = (List<double>)ctx.LoadObject("SellModel") ?? new List<double>();
            if (modelSellList.Any())
            {
                var sellList = ValidateSellModel(source, modelSellList, actualBar);
                foreach (double value in sellList)
                {
                    source.Positions.SellIfLess(actualBar + 1, Value, value + ScopeDelta, Slippage, "sell_" + value);
                }
                ctx.StoreObject("SellList", sellList);
            }
        }

        public void SearchBuyModel(IContext ctx, ISecurity compressSource, int indexCompressBar, int indexBeginDayBar, int actualBar, List<double> buySignal)
        {
            var modelBuyList = new List<double>();

            for (var indexPointA = indexCompressBar - 1; indexPointA >= indexBeginDayBar && indexPointA >= 0; indexPointA--)
            {
                var pointB = compressSource.HighPrices.
                    Select((value, index) => new { Value = value, Index = index }).
                    Skip(indexPointA).
                    Take(indexCompressBar - indexPointA + 1).
                    OrderBy(x => x.Value).ThenBy(x => x.Index).Last();

                var realPointA = compressSource.LowPrices.
                    Select((value, index) => new { Value = value, Index = index }).
                    Skip(indexPointA).
                    Take(pointB.Index - indexPointA + 1).
                    OrderBy(x => x.Value).ThenByDescending(x => x.Index).First();

                // Точки A и B не могут быть на одном баре
                if (pointB.Index == realPointA.Index) continue;

                // Проверм размер фигуры A-B
                var ab = pointB.Value - realPointA.Value;
                if (ab <= LengthSegmentBC || ab >= LengthSegmentAB) continue;

                var pointC = compressSource.LowPrices.
                    Select((value, index) => new { Value = value, Index = index }).
                    Skip(pointB.Index).
                    Take(indexCompressBar - pointB.Index + 1).
                    OrderBy(x => x.Value).ThenByDescending(x => x.Index).First();

                // Точки B и C не могут быть на одном баре
                if (pointB.Index == pointC.Index) continue;

                // Проверям размер модели B-C
                if (pointB.Value - pointC.Value <= LengthSegmentBC ||
                    pointC.Value - realPointA.Value < 0) continue;

                // Проверка на пересечение
                if (indexCompressBar != pointC.Index)
                {
                    var validateMax = compressSource.HighPrices.
                        Skip(pointC.Index + 1).
                        Take(indexCompressBar - pointC.Index).
                        Max();
                    if (pointB.Value - ScopeDelta <= validateMax) continue;
                }

                modelBuyList.Add(pointB.Value);

                buySignal[actualBar] = 1;
            }

            ctx.StoreObject("BuyModel", modelBuyList);
        }

        public void SearchSellModel(IContext ctx, ISecurity compressSource, int indexCompressBar, int indexBeginDayBar, int actualBar, List<double> sellSignal)
        {
            var modelSellList = new List<double>();

            for (var indexPointA = indexCompressBar - 1; indexPointA >= indexBeginDayBar && indexPointA >= 0; indexPointA--)
            {
                var pointB = compressSource.LowPrices.
                    Select((value, index) => new { Value = value, Index = index }).
                    Skip(indexPointA).
                    Take(indexCompressBar - indexPointA + 1).
                    OrderBy(x => x.Value).First();

                var realPointA = compressSource.HighPrices.
                    Select((value, index) => new { Value = value, Index = index }).
                    Skip(indexPointA).
                    Take(pointB.Index - indexPointA + 1).
                    OrderBy(x => x.Value).Last();

                // Точки A и B не могут быть на одном баре
                if (pointB.Index == realPointA.Index) continue;

                // Проверм размер фигуры A-B
                var ab = realPointA.Value - pointB.Value;
                if (ab <= LengthSegmentBC || ab >= LengthSegmentAB) continue;

                var pointC = compressSource.HighPrices.
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
                    var validateMin = compressSource.LowPrices.
                        Skip(pointC.Index + 1).
                        Take(indexCompressBar - pointC.Index).
                        Min();
                    if (pointB.Value + ScopeDelta >= validateMin) continue;
                }

                modelSellList.Add(pointB.Value);

                sellSignal[actualBar] = 1;
            }

            ctx.StoreObject("SellModel", modelSellList);
        }

        public List<double> ValidateBuyModel(ISecurity source, List<double> modelBuyList, int actualBar)
        {
            double lastMax = double.MinValue;

            for (var i = actualBar; i >= 0 && (source.Bars[i].Date.TimeOfDay.TotalSeconds + 5) % 300 != 0; i--)
            {
                lastMax = source.HighPrices[i] > lastMax ? source.HighPrices[i] : lastMax;
            }

            return modelBuyList.Where(value => value - ScopeDelta > lastMax).ToList();
        }

        private List<double> ValidateSellModel(ISecurity source, List<double> modelSellList, int actualBar)
        {
            double lastMin = double.MaxValue;

            for (var i = actualBar; i >= 0 && (source.Bars[i].Date.TimeOfDay.TotalSeconds + 5) % 300 != 0; i--)
            {
                lastMin = source.LowPrices[i] < lastMin ? source.LowPrices[i] : lastMin;
            }

            return modelSellList.Where(value => value + ScopeDelta < lastMin).ToList();
        }

        public void SearchActivePosition(ISecurity source, int actualBar)
        {
            var positionList = source.Positions.GetActiveForBar(actualBar);

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

        private void CloseAllPosition(ISecurity source, int actualBar)
        {
            var positionList = source.Positions;

            foreach (var position in positionList)
            {
                if (position.IsActive)
                {
                    position.CloseAtMarket(actualBar + 1, "closeAtTime");
                }
            }
        }

        public static bool GetValidTimeFrame(IContext ctx, ISecurity source)
        {
            if (source.IntervalBase == DataIntervals.SECONDS && source.Interval == 5) return true;
            ctx.Log("Выбран не верный таймфрейм, выберите таймфрейм равный 5 секундам", new Color(255, 0, 0), true);
            return false;
        }

        private int GetIndexCompressBar(ISecurity compressSource, DateTime dateActualBar, int indexBeginDayBar)
        {
            var indexCompressBar = indexBeginDayBar;
            var tempTime = dateActualBar - FiveMinutes - FiveSeconds;
            while (compressSource.Bars[indexCompressBar].Date < tempTime)
            {
                indexCompressBar++;
            }

            return indexCompressBar;
        }
    }
}
