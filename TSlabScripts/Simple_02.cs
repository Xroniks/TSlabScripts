using System;
using System.Linq;
using MoreLinq;
using System.Collections.Generic;
using TSLab.DataSource;
using TSLab.Script;
using TSLab.Script.Handlers;
using TSLab.Script.Optimization;

namespace TSLabScripts
{
    public class Simple_02 : IExternalScript
    {
        public OptimProperty Slippage = new OptimProperty(30, 0, 100, 10); // Проскальзывание
        public OptimProperty Value = new OptimProperty(1, 0, 100, 10); // Колличество контрактов

        public OptimProperty LengthSegmentAB = new OptimProperty(0, 0, 5000, 10); // При нуле настройка выключена
        public OptimProperty MinLengthSegmentBC = new OptimProperty(300, 0, 5000, 10);
        public OptimProperty MaxLengthSegmentBC = new OptimProperty(0, 0, 5000, 10); // При нуле настройка выключена

        public OptimProperty MultyplayDelta = new OptimProperty(0.1, 0, 1, 0.01);
        public OptimProperty MultyplayProfit = new OptimProperty(0.2, 0, 1, 0.01);
        public OptimProperty MultyplayStop = new OptimProperty(0.6, 0, 1, 0.01);

        public static OptimProperty DeltaModelSpanSeconds = new OptimProperty(0, 0, 86400, 5); // При нуле настройка выключена
        public static OptimProperty DeltaPositionSpanSeconds = new OptimProperty(0, 0, 86400, 5); // При нуле настройка выключена

        public TimeSpan TimeCloseAllPosition = new TimeSpan(18, 40, 00);
        public TimeSpan TimeBeginDayBar = new TimeSpan(10, 00, 00);
        public TimeSpan TimeBeginBar = new TimeSpan(10, 04, 55);
        public TimeSpan FiveSeconds = new TimeSpan(0, 0, 5);
        public TimeSpan FiveMinutes = new TimeSpan(0, 5, 0);
        public TimeSpan DeltaModelTimeSpan = new TimeSpan(0, 0, DeltaModelSpanSeconds);
        public TimeSpan DeltaPositionTimeSpan = new TimeSpan(0, 0, DeltaPositionSpanSeconds);

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

            // Генерируем пустые последовательности для дальнейшего заполнения сигналами
            var buySignal = new List<double>();
            var sellSignal = new List<double>();
            for (int i = 0; i < source.Bars.Count; i++)
            {
                buySignal.Add(0);
                sellSignal.Add(0);
            }

            // Цикл для торговли
            for (var historyBar = 1; historyBar <= source.Bars.Count - 1; historyBar++)
            {
                Trading(ctx, source, compressSource, historyBar, buySignal, sellSignal);
            }
            
            // Отображение последовательностей с моделями (заполняются при поиске моделей)
            var buyPain = ctx.CreatePane("BuySignal", 15, false);
            buyPain.AddList("BuySignal", buySignal, ListStyles.HISTOHRAM_FILL, new Color(0, 255, 0), LineStyles.SOLID,
                PaneSides.RIGHT);

            var sellPain = ctx.CreatePane("SellSignal", 15, false);
            sellPain.AddList("SellSignal", sellSignal, ListStyles.HISTOHRAM_FILL, new Color(255, 0, 0), LineStyles.SOLID,
                PaneSides.RIGHT);
        }

        public void Trading(IContext ctx, ISecurity source, ISecurity compressSource, int actualBar, List<double> buySignal, List<double> sellSignal)
        {
            // Не торговать ранее 10:30, есть исторические данные с более ранними тиками
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

            // Посик активных позиций и выставление по ним стопов
            if (source.Positions.ActivePositionCount > 0)
            {
                SearchActivePosition(source, actualBar);
            }

            // Расчет модели происходит только на последнем пятисекундном баре в пятиминутном баре (пересчет один раз в пять минут)
            var totalSecondsActualBar = source.Bars[actualBar].Date.TimeOfDay.TotalSeconds;
            if ((totalSecondsActualBar + 5) % 300 == 0)
            {
                var dateActualBar = source.Bars[actualBar].Date;
                int indexBeginDayBar = GetIndexBeginDayBar(compressSource, dateActualBar);

                int indexCompressBar = GetIndexCompressBar(compressSource, dateActualBar, indexBeginDayBar);

                SearchBuyModel(ctx, compressSource, indexCompressBar, indexBeginDayBar, actualBar, buySignal);

                SearchSellModel(ctx, compressSource, indexCompressBar, indexBeginDayBar, actualBar, sellSignal);
            }

            // Читаем из кеша список моделей и выставляем ордера для существующих моделей
            var modelBuyList = (List<TradingModel>)ctx.LoadObject("BuyModel") ?? new List<TradingModel>();
            if (modelBuyList.Any())
            {
                var buyList = ValidateBuyModel(source, modelBuyList, actualBar);
                foreach (TradingModel model in buyList)
                {
                    source.Positions.BuyIfGreater(actualBar + 1, Value, model.Value - ScopeDelta, Slippage, "buy_" + model.Value);
                }
                ctx.StoreObject("BuyModel", buyList);
            }

            var modelSellList = (List<TradingModel>)ctx.LoadObject("SellModel") ?? new List<TradingModel>();
            if (modelSellList.Any())
            {
                var sellList = ValidateSellModel(source, modelSellList, actualBar);
                foreach (TradingModel model in sellList)
                {
                    source.Positions.SellIfLess(actualBar + 1, Value, model.Value + ScopeDelta, Slippage, "sell_" + model.Value);
                }
                ctx.StoreObject("SellList", sellList);
            }
        }

        public void SearchBuyModel(IContext ctx, ISecurity compressSource, int indexCompressBar, int indexBeginDayBar, int actualBar, List<double> buySignal)
        {
            var modelBuyList = new List<TradingModel>();

            for (var indexPointA = indexCompressBar - 1; indexPointA >= indexBeginDayBar && indexPointA >= 0; indexPointA--)
            {
                var pointB = compressSource.HighPrices.
                    Select((value, index) => new { Value = value, Index = index }).
                    Skip(indexPointA).
                    Take(indexCompressBar - indexPointA + 1).
                    MaxBy(item => item.Value);

                var realPointA = compressSource.LowPrices.
                    Select((value, index) => new { Value = value, Index = index }).
                    Skip(indexPointA).
                    Take(pointB.Index - indexPointA + 1).
                    MinBy(item => item.Value);

                // Точки A и B не могут быть на одном баре
                if (pointB.Index == realPointA.Index)
                {
                    continue;
                }

                // Проверм размер фигуры A-B
                var ab = pointB.Value - realPointA.Value;
                if (ab <= MinLengthSegmentBC || (LengthSegmentAB != 0 && ab >= LengthSegmentAB))
                {
                    continue;
                }

                var pointC = compressSource.LowPrices.
                    Select((value, index) => new { Value = value, Index = index }).
                    Skip(pointB.Index).
                    Take(indexCompressBar - pointB.Index + 1).
                    MinBy(item => item.Value);

                // Точки B и C не могут быть на одном баре
                if (pointB.Index == pointC.Index) continue;

                // Проверям размер модели B-C
                if (pointB.Value - pointC.Value <= MinLengthSegmentBC
                    || (MaxLengthSegmentBC != 0 && pointB.Value - pointC.Value >= MaxLengthSegmentBC)
                    || pointC.Value - realPointA.Value < 0)
                {
                    continue;
                }

                // Проверка на пересечение
                if (indexCompressBar != pointC.Index)
                { 
                    var validateMax = compressSource.HighPrices.
                        Skip(pointC.Index + 1).
                        Take(indexCompressBar - pointC.Index).
                        Max();
                    if (pointB.Value - ScopeDelta <= validateMax) continue;
                }

                // Проверка на время модели
                if (DeltaModelTimeSpan != new TimeSpan(0, 0, 0) &&
                    compressSource.Bars[indexCompressBar].Date - compressSource.Bars[pointB.Index].Date > DeltaModelTimeSpan)
                {
                    continue;
                }

                modelBuyList.Add(new TradingModel
                {
                    Value = pointB.Value
                });

                buySignal[actualBar] = 1;
            }

            ctx.StoreObject("BuyModel", modelBuyList);
        }

        public void SearchSellModel(IContext ctx, ISecurity compressSource, int indexCompressBar, int indexBeginDayBar, int actualBar, List<double> sellSignal)
        {
            List<TradingModel> modelSellList = new List<TradingModel>();

            for (var indexPointA = indexCompressBar - 1; indexPointA >= indexBeginDayBar && indexPointA >= 0; indexPointA--)
            {
                var pointB = compressSource.LowPrices.
                    Select((value, index) => new { Value = value, Index = index }).
                    Skip(indexPointA).
                    Take(indexCompressBar - indexPointA + 1).
                    MinBy(item => item.Value);

                var realPointA = compressSource.HighPrices.
                    Select((value, index) => new { Value = value, Index = index }).
                    Skip(indexPointA).
                    Take(pointB.Index - indexPointA + 1).
                    MaxBy(item => item.Value);

                // Точки A и B не могут быть на одном баре
                if (pointB.Index == realPointA.Index)
                {
                    continue;
                }

                // Проверм размер фигуры A-B
                var ab = realPointA.Value - pointB.Value;
                if (ab <= MinLengthSegmentBC || (LengthSegmentAB != 0 && ab >= LengthSegmentAB))
                {
                    continue;
                }

                var pointC = compressSource.HighPrices.
                    Select((value, index) => new { Value = value, Index = index }).
                    Skip(pointB.Index).
                    Take(indexCompressBar - pointB.Index + 1).
                    MaxBy(item => item.Value);

                // Точки B и C не могут быть на одном баре
                if (pointB.Index == pointC.Index) continue;

                // Проверям размер модели B-C
                if (pointC.Value - pointB.Value <= MinLengthSegmentBC
                    || (MaxLengthSegmentBC != 0 && pointC.Value - pointB.Value >= MaxLengthSegmentBC)
                    || realPointA.Value - pointC.Value < 0)
                {
                    continue;
                }

                // Проверка на пересечение
                if (indexCompressBar != pointC.Index)
                {
                    var validateMin = compressSource.LowPrices.
                        Skip(pointC.Index + 1).
                        Take(indexCompressBar - pointC.Index).
                        Min();
                    if (pointB.Value + ScopeDelta >= validateMin) continue;
                }

                // Проверка на время модели
                if (DeltaModelTimeSpan != new TimeSpan(0, 0, 0) &&
                    compressSource.Bars[indexCompressBar].Date - compressSource.Bars[pointB.Index].Date > DeltaModelTimeSpan)
                {
                    continue;
                }

                modelSellList.Add(new TradingModel
                {
                    Value = pointB.Value
                });

                sellSignal[actualBar] = 1;
            }

            ctx.StoreObject("SellModel", modelSellList);
        }

        public List<TradingModel> ValidateBuyModel(ISecurity source, List<TradingModel> modelBuyList, int actualBar)
        {
            double lastMax = double.MinValue;

            for (var i = actualBar; i >= 0 && (source.Bars[i].Date.TimeOfDay.TotalSeconds + 5) % 300 != 0; i--)
            {
                lastMax = source.HighPrices[i] > lastMax ? source.HighPrices[i] : lastMax;
            }

            return modelBuyList.Where(model => model.Value - ScopeDelta > lastMax).ToList();
        }

        private List<TradingModel> ValidateSellModel(ISecurity source, List<TradingModel> modelSellList, int actualBar)
        {
            double lastMin = double.MaxValue;

            for (var i = actualBar; i >= 0 && (source.Bars[i].Date.TimeOfDay.TotalSeconds + 5) % 300 != 0; i--)
            {
                lastMin = source.LowPrices[i] < lastMin ? source.LowPrices[i] : lastMin;
            }

            return modelSellList.Where(model => model.Value + ScopeDelta < lastMin).ToList();
        }

        public void SearchActivePosition(ISecurity source, int actualBar)
        {
            var positionList = source.Positions.GetActiveForBar(actualBar);

            foreach (var position in positionList)
            {
                if (DeltaPositionTimeSpan != new TimeSpan(0, 0, 0) &&
                    source.Bars[actualBar].Date - position.EntryBar.Date >= DeltaPositionTimeSpan)
                {
                    position.CloseAtMarket(actualBar + 1, "closeAtTime");
                    continue;
                }
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

        public void CloseAllPosition(ISecurity source, int actualBar)
        {
            var positionList = source.Positions.GetActiveForBar(actualBar);

            foreach (var position in positionList)
            {
                position.CloseAtMarket(actualBar + 1, "closeAtTime");
            }
        }

        public bool GetValidTimeFrame(IContext ctx, ISecurity source)
        {
            if (source.IntervalBase == DataIntervals.SECONDS && source.Interval == 5) return true;
            ctx.Log("Выбран не верный таймфрейм, выберите таймфрейм равный 5 секундам", new Color(255, 0, 0), true);
            return false;
        }

        /// <summary>
        /// Ищет индекс, которому 
        /// </summary>
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

        /// <summary>
        /// Ищет индекс первого бара текущего дня, начинает искать с 10:00, если такого нет, то добавляет пять минут и снова ищет.
        /// </summary>
        private int GetIndexBeginDayBar(ISecurity compressSource, DateTime dateActualBar)
        {
            do
            {
                int indexBeginDayBar;

                try
                {
                    indexBeginDayBar = compressSource.Bars
                        .Select((bar, index) => new {Index = index, Bar = bar})
                        .Last(item =>
                            item.Bar.Date.TimeOfDay == TimeBeginDayBar &&
                            item.Bar.Date.Day == dateActualBar.Day &&
                            item.Bar.Date.Month == dateActualBar.Month &&
                            item.Bar.Date.Year == dateActualBar.Year).Index;
                }
                catch (Exception e)
                {
                    TimeBeginDayBar = TimeBeginDayBar.Add(FiveMinutes);
                    continue;
                }

                return indexBeginDayBar;

            } while (true);
        }
    }

    public struct TradingModel
    {
        public double Value { get; set; }

        public double EnterPrice { get; set; }

        public double ProfitPrice { get; set; }

        public double StopPrice { get; set; }
    }
}
