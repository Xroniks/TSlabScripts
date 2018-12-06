using MoreLinq;
using System;
using System.Collections.Generic;
using System.Linq;
using TSLab.DataSource;
using TSLab.Script;
using TSLab.Script.Handlers;
using TSLab.Script.Optimization;

namespace TSLabScripts
{
    public class CoefficientForFiveMinutes : IExternalScript
    {
        public static OptimProperty DeltaModelSpanSeconds = new OptimProperty(double.MinValue, double.MinValue, 86400.0, 5.0);
        public static OptimProperty DeltaPositionSpanSeconds = new OptimProperty(double.MinValue, double.MinValue, 86400.0, 5.0);

        public OptimProperty Slippage = new OptimProperty(30.0, double.MinValue, 100.0, 10.0);
        public OptimProperty Value = new OptimProperty(1.0, double.MinValue, 100.0, 10.0);
        public OptimProperty LengthSegmentAB = new OptimProperty(double.MinValue, double.MinValue, 5000.0, 10.0);
        public OptimProperty MinLengthSegmentBC = new OptimProperty(300.0, double.MinValue, 5000.0, 10.0);
        public OptimProperty MaxLengthSegmentBC = new OptimProperty(double.MinValue, double.MinValue, 5000.0, 10.0);
        public OptimProperty MultyplayDelta = new OptimProperty(1.03, 1.0, 2.0, double.MaxValue);
        public OptimProperty MultyplayProfit = new OptimProperty(1011.0 / 1000.0, 1.0, 2.0, double.MaxValue);
        public OptimProperty MultyplayStop = new OptimProperty(1.0065, 1.0, 2.0, double.MaxValue);

        private TimeSpan TimeCloseAllPosition = new TimeSpan(18, 40, 0);
        private TimeSpan TimeBeginDayBar = new TimeSpan(10, 0, 0);
        private TimeSpan TimeBeginBar = new TimeSpan(10, 4, 55);
        private TimeSpan FiveSeconds = new TimeSpan(0, 0, 5);
        private TimeSpan FiveMinutes = new TimeSpan(0, 5, 0);
        private TimeSpan DeltaModelTimeSpan = new TimeSpan(0, 0, DeltaModelSpanSeconds);
        private TimeSpan DeltaPositionTimeSpan = new TimeSpan(0, 0, DeltaPositionSpanSeconds);

        public virtual void Execute(IContext ctx, ISecurity source)
        {
            // Проверяем таймфрейм входных данных
            if (!GetValidTimeFrame(ctx, source)) return;
            
            // Компрессия исходного таймфрейма в пятиминутный
            var security = source.CompressTo(new Interval(5, DataIntervals.MINUTE), 0, 200, 0);
            
            // Генерация графика исходного таймфрейма
            var pane = ctx.CreatePane("Original", 70.0, false);
            pane.AddList(source.Symbol, security, CandleStyles.BAR_CANDLE, new Color(100, 100, 100), PaneSides.RIGHT);
            pane.AddList(source.Symbol, source, CandleStyles.BAR_CANDLE, new Color(0, 0, 0), PaneSides.RIGHT);
            
            var buySignal = new List<double>();
            var sellSignal = new List<double>();
            for (var index = 0; index < source.Bars.Count; ++index)
            {
                buySignal.Add(0.0);
                sellSignal.Add(0.0);
            }

            for (var actualBar = 1; actualBar <= source.Bars.Count - 1; ++actualBar)
            {
                Trading(ctx, source, security, actualBar, buySignal, sellSignal);
            }

            ctx.CreatePane("BuySignal", 15.0, false)
                .AddList("BuySignal", buySignal, ListStyles.HISTOHRAM_FILL, new Color(0, 255, 0), LineStyles.SOLID, PaneSides.RIGHT);
            ctx.CreatePane("SellSignal", 15.0, false)
                .AddList("SellSignal", sellSignal, ListStyles.HISTOHRAM_FILL, new Color(255, 0, 0), LineStyles.SOLID, PaneSides.RIGHT);
        }

        private void Trading(
            IContext ctx, 
            ISecurity source, 
            ISecurity compressSource, 
            int actualBar,
            List<double> buySignal, 
            List<double> sellSignal)
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
            
            if (IsClosedBar(source.Bars[actualBar]))
            {
                var date = source.Bars[actualBar].Date;
                var indexBeginDayBar = GetIndexBeginDayBar(compressSource, date);
                var indexCompressBar = GetIndexCompressBar(compressSource, date, indexBeginDayBar);
                SearchBuyModel(ctx, compressSource, indexCompressBar, indexBeginDayBar, actualBar, buySignal);
                SearchSellModel(ctx, compressSource, indexCompressBar, indexBeginDayBar, actualBar, sellSignal);
            }

            var modelBuyList = (List<TradingModel>)ctx.LoadObject("BuyModel") ?? new List<TradingModel>();
            if (modelBuyList.Any())
            {
                var validateBuyModels = ValidateBuyModel(source, modelBuyList, actualBar);
                foreach (var tradingModel in validateBuyModels)
                {
                    var namePosition = $"buy_{tradingModel.GetNamePosition}";
                    source.Positions.BuyIfGreater(actualBar + 1, Convert.ToDouble(Value), Convert.ToDouble(tradingModel.EnterPrice), Convert.ToDouble(Slippage), namePosition);
                }

                ctx.StoreObject("BuyModel", validateBuyModels);
            }

            var modelSellList = (List<TradingModel>)ctx.LoadObject("SellModel") ?? new List<TradingModel>();
            if (modelSellList.Any())
            {
                var validateSellModels = ValidateSellModel(source, modelSellList, actualBar);
                foreach (var tradingModel in validateSellModels)
                {
                    var namePosition = $"sell_{tradingModel.GetNamePosition}";
                    source.Positions.SellIfLess(actualBar + 1, Convert.ToDouble(Value), Convert.ToDouble(tradingModel.EnterPrice), Convert.ToDouble(Slippage), namePosition);
                }

                ctx.StoreObject("SellList", validateSellModels);
            }
        }

        private void SearchBuyModel(
            IContext ctx, 
            ISecurity compressSource, 
            int indexCompressBar, 
            int indexBeginDayBar,
            int actualBar, 
            List<double> buySignal)
        {
            var tradingModelList = new List<TradingModel>();
            
            for (var indexPointA = indexCompressBar - 1; indexPointA >= indexBeginDayBar && indexPointA >= 0; --indexPointA)
            {
                var pointB = compressSource.HighPrices
                    .Select((value, index) => new { Value = value, Index = index })
                    .Skip(indexPointA)
                    .Take(indexCompressBar - indexPointA + 1)
                    .MaxBy(item => item.Value);
                
                var realPointA = compressSource.LowPrices
                    .Select((value, index) => new { Value = value, Index = index })
                    .Skip(indexPointA)
                    .Take(pointB.Index - indexPointA + 1)
                    .MinBy(item => item.Value);
                
                // Точки A и B не могут быть на одном баре
                if (pointB.Index == realPointA.Index) continue;
                
                // Проверм размер фигуры A-B
                var ab = pointB.Value - realPointA.Value;
                if (ab <= MinLengthSegmentBC || (LengthSegmentAB != 0 && ab >= LengthSegmentAB)) continue;
                
                var pointC = compressSource.LowPrices
                    .Select((value, index) => new { Value = value, Index = index })
                    .Skip(pointB.Index)
                    .Take(indexCompressBar - pointB.Index + 1)
                    .MinBy(item => item.Value);
                
                // Точки B и C не могут быть на одном баре
                if (pointB.Index == pointC.Index) continue;
               
                // Проверям размер модели B-C
                var bc = pointB.Value - pointC.Value;
                if (bc <= MinLengthSegmentBC || 
                    (MaxLengthSegmentBC != 0 && bc >= MaxLengthSegmentBC) ||
                    pointC.Value - realPointA.Value < 0) continue;
                
                var newBuyTradingModel = GetNewBuyTradingModel(pointB.Value, bc);
                if (indexCompressBar != pointC.Index)
                {
                    var max = compressSource.HighPrices
                        .Skip(pointC.Index + 1)
                        .Take(indexCompressBar - pointC.Index)
                        .Max();
                    
                    if (newBuyTradingModel.EnterPrice <= max) continue;
                }

                if (DeltaModelTimeSpan == new TimeSpan(0, 0, 0) ||
                    compressSource.Bars[indexCompressBar].Date - compressSource.Bars[pointB.Index].Date <= DeltaModelTimeSpan)
                {
                    tradingModelList.Add(newBuyTradingModel);
                    buySignal[actualBar] = 1;
                }
            }

            ctx.StoreObject("BuyModel", tradingModelList);
        }

        private void SearchSellModel(
            IContext ctx, 
            ISecurity compressSource, 
            int indexCompressBar, 
            int indexBeginDayBar,
            int actualBar, 
            List<double> sellSignal)
        {
            var tradingModelList = new List<TradingModel>();
            
            for (var indexPointA = indexCompressBar - 1; indexPointA >= indexBeginDayBar && indexPointA >= 0; --indexPointA)
            {
                var pointB = compressSource.LowPrices
                    .Select((value, index) => new { Value = value, Index = index })
                    .Skip(indexPointA)
                    .Take(indexCompressBar - indexPointA + 1)
                    .MinBy(item => item.Value);
                
                var realPointA = compressSource.HighPrices
                    .Select((value, index) => new { Value = value, Index = index })
                    .Skip(indexPointA)
                    .Take(pointB.Index - indexPointA + 1)
                    .MaxBy(item => item.Value);
                
                // Точки A и B не могут быть на одном баре
                if (pointB.Index == realPointA.Index) continue;
                
                // Проверм размер фигуры A-B
                var ab = realPointA.Value - pointB.Value;
                if (ab <= MinLengthSegmentBC || (LengthSegmentAB != 0 && ab >= LengthSegmentAB)) continue;
                
                var pointC = compressSource.HighPrices
                    .Select((value, index) => new { Value = value, Index = index })
                    .Skip(pointB.Index)
                    .Take(indexCompressBar - pointB.Index + 1)
                    .MaxBy(item => item.Value);
                
                // Точки B и C не могут быть на одном баре
                if (pointB.Index == pointC.Index) continue;
                
                // Проверям размер модели B-C
                var bc = pointC.Value - pointB.Value;
                if (bc <= MinLengthSegmentBC || 
                    (MaxLengthSegmentBC != 0 && bc >= MaxLengthSegmentBC) ||
                    realPointA.Value - pointC.Value < 0) continue;
                
                var sellTradingModel = GetNewSellTradingModel(pointB.Value, bc);
                if (indexCompressBar != pointC.Index)
                {
                    var min = compressSource.LowPrices
                        .Skip(pointC.Index + 1)
                        .Take(indexCompressBar - pointC.Index)
                        .Min();
                    
                    if (sellTradingModel.EnterPrice >= min) continue;
                }

                if (DeltaModelTimeSpan == new TimeSpan(0, 0, 0) ||
                    compressSource.Bars[indexCompressBar].Date - compressSource.Bars[pointB.Index].Date <= DeltaModelTimeSpan)
                {
                    tradingModelList.Add(sellTradingModel);
                    sellSignal[actualBar] = 1;
                }
            }

            ctx.StoreObject("SellModel", tradingModelList);
        }

        private List<TradingModel> ValidateBuyModel(
            ISecurity source, 
            List<TradingModel> modelBuyList,
            int actualBar)
        {
            var lastMax = double.MinValue;
            
            for (var index = actualBar; index >= 0 && !IsClosedBar(source.Bars[index]); --index)
            {
                lastMax = source.HighPrices[index] > lastMax ? source.HighPrices[index] : lastMax;
            }

            return modelBuyList.Where(model => model.EnterPrice > lastMax).ToList();
        }

        private List<TradingModel> ValidateSellModel(
            ISecurity source, 
            List<TradingModel> modelSellList,
            int actualBar)
        {
            var lastMin = double.MaxValue;

            for (var index = actualBar; index >= 0 && !IsClosedBar(source.Bars[index]); --index)
            {
                lastMin = source.LowPrices[index] < lastMin ? source.LowPrices[index] : lastMin;
            }

            return modelSellList.Where(model => model.EnterPrice < lastMin).ToList();
        }

        private void SearchActivePosition(ISecurity source, int actualBar)
        {
            foreach (var position in source.Positions.GetActiveForBar(actualBar))
            {
                if (DeltaPositionTimeSpan != new TimeSpan(0, 0, 0) &&
                    source.Bars[actualBar].Date - position.EntryBar.Date >= DeltaPositionTimeSpan)
                {
                    position.CloseAtMarket(actualBar + 1, "closeAtTime");
                }
                else
                {
                    var strArray = position.EntrySignalName.Split('_');
                    switch (strArray[0])
                    {
                        case "buy":
                            position.CloseAtStop(actualBar + 1, Convert.ToDouble(strArray[3]), Convert.ToDouble(Slippage), "closeStop");
                            position.CloseAtProfit(actualBar + 1, Convert.ToDouble(strArray[4]), "closeProfit");
                            break;
                        case "sell":
                            position.CloseAtStop(actualBar + 1, Convert.ToDouble(strArray[3]), Convert.ToDouble(Slippage), "closeStop");
                            position.CloseAtProfit(actualBar + 1, Convert.ToDouble(strArray[4]), "closeProfit");
                            break;
                    }
                }
            }
        }

        private void CloseAllPosition(ISecurity source, int actualBar)
        {
            foreach (var position in source.Positions.GetActiveForBar(actualBar))
                position.CloseAtMarket(actualBar + 1, "closeAtTime");
        }

        private bool GetValidTimeFrame(IContext ctx, ISecurity source)
        {
            if (source.IntervalBase == DataIntervals.SECONDS && source.Interval == 5)
            {
                return true;
            }

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

        private int GetIndexBeginDayBar(ISecurity compressSource, DateTime dateActualBar)
        {
            while (true)
            {
                try
                {
                    return compressSource.Bars
                        .Select((bar, index) => new { Index = index, Bar = bar })
                        .Last(item => 
                            item.Bar.Date.TimeOfDay == TimeBeginDayBar &&
                            item.Bar.Date.Day == dateActualBar.Day &&
                            item.Bar.Date.Month == dateActualBar.Month &&
                            item.Bar.Date.Year == dateActualBar.Year).Index;
                }
                catch
                {
                    TimeBeginDayBar = TimeBeginDayBar.Add(FiveMinutes);
                }
            }
        }

        private static bool IsClosedBar(Bar bar)
        {
            return (bar.Date.TimeOfDay.TotalSeconds + 5.0) % 300.0 == 0.0;
        }

        private TradingModel GetNewBuyTradingModel(double value, double bc)
        {
            return new TradingModel
            {
                Value = value,
                EnterPrice = value - Math.Round(Math.Log(bc / 100.0, MultyplayDelta) / 10.0, 0) * 10.0,
                StopPrice = value - Math.Round(Math.Log(bc / 100.0, MultyplayStop) / 10.0, 0) * 10.0,
                ProfitPrice = value + Math.Round(Math.Log(bc / 100.0, MultyplayProfit) / 10.0, 0) * 10.0
            };
        }

        private TradingModel GetNewSellTradingModel(double value, double bc)
        {
            return new TradingModel
            {
                Value = value,
                EnterPrice = value + Math.Round(Math.Log(bc / 100.0, MultyplayDelta) / 10.0, 0) * 10.0,
                StopPrice = value + Math.Round(Math.Log(bc / 100.0, MultyplayStop) / 10.0, 0) * 10.0,
                ProfitPrice = value - Math.Round(Math.Log(bc / 100.0, MultyplayProfit) / 10.0, 0) * 10.0
            };
        }

        private class TradingModel
        {
            public double Value { get; set; }

            public double EnterPrice { get; set; }

            public double StopPrice { get; set; }

            public double ProfitPrice { get; set; }

            public string GetNamePosition => $"{Value}_{EnterPrice}_{StopPrice}_{ProfitPrice}";
        }
    }
}