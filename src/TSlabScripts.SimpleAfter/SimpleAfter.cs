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
        public OptimProperty Value = new OptimProperty(1, 0, 100, 1);
        public OptimProperty LengthSegmentAB = new OptimProperty(1000, 0, double.MaxValue, 10);
        public OptimProperty LengthSegmentBC = new OptimProperty(390, 0, double.MaxValue, 10);
        public OptimProperty ScopeDeltaSimple = new OptimProperty(50, 0, double.MaxValue, 10);
        public OptimProperty ScopeProfiteSimple = new OptimProperty(100, 0, double.MaxValue, 10);
        public OptimProperty ScopeStopeSimple = new OptimProperty(300, 0, double.MaxValue, 10);

        public OptimProperty WeitCountBar = new OptimProperty(10, 0, 1000, 1);
        public OptimProperty ScopeProfiteAfteSimple = new OptimProperty(200, 0, double.MaxValue, 10);
        public OptimProperty ScopeStopeAfteSimple = new OptimProperty(250, 0, double.MaxValue, 10);

        private readonly TimeSpan TimeCloseAllPosition = new TimeSpan(18, 40, 00);
        private readonly TimeSpan TimeBeginBar = new TimeSpan(10, 04, 55);

        private static IContext TsLabContext { get; set; }
        private static ISecurity TsLabSource { get; set; }
        private static ISecurity TsLabCompressSource { get; set; }
        private ModelSignal Model { get; set; }

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
            Model = new ModelSignal(TsLabSource.Bars.Count);

            for (var historyBar = 1; historyBar <= TsLabSource.Bars.Count - 1; historyBar++)
            {
                Trading(historyBar);
            }

            SimpleHealper.RenderModelIndicator(TsLabContext, Model);
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

                SearchSellModel(indexCompressBar, indexBeginDayBar, actualBar);
                SearchBuyModel(indexCompressBar, indexBeginDayBar, actualBar);
            }

            SetStopToOpenPosition(actualBar);
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
                        position.CloseAtProfit(actualBar + 1, Convert.ToDouble(arr[1]) + ScopeProfiteAfteSimple, "closeProfit");
                        position.CloseAtStop(actualBar + 1, Convert.ToDouble(arr[1]) - ScopeStopeAfteSimple, Slippage, "closeStop");
                        break;
                    case "sell":
                        position.CloseAtProfit(actualBar + 1, Convert.ToDouble(arr[1]) - ScopeProfiteAfteSimple, "closeProfit");
                        position.CloseAtStop(actualBar + 1, Convert.ToDouble(arr[1]) + ScopeStopeAfteSimple, Slippage, "closeStop");
                        break;
                }
            }
        }

        private void SearchSellModel(int indexCompressBar, int indexBeginDayBar, int actualBar)
        {
            var modelSellList = new List<double>();

            for (var indexPointA = indexCompressBar - 1; indexPointA >= indexBeginDayBar && indexPointA >= 0; indexPointA--)
            {
                var pointC = SimpleService.GetLowPrices(TsLabCompressSource.LowPrices, indexPointA, indexCompressBar);
                var pointB = SimpleService.GetHighPrices(TsLabCompressSource.HighPrices, indexPointA, pointC.Index);
                var realPointA = SimpleService.GetLowPrices(TsLabCompressSource.LowPrices, indexPointA, pointB.Index);

                // Точки A и B не могут быть на одном баре
                if (pointB.Index == realPointA.Index) continue;

                // Проверм размер фигуры A-B
                var ab = pointB.Value - realPointA.Value;
                if (ab <= LengthSegmentBC || ab >= LengthSegmentAB) continue;

                // Точки B и C не могут быть на одном баре
                if (pointB.Index == pointC.Index) continue;

                // Проверям размер модели B-C
                if (pointB.Value - pointC.Value <= LengthSegmentBC ||
                    pointC.Value - realPointA.Value < 0) continue;

                // Входа по Simple еще не было
                if (indexCompressBar == pointC.Index) continue;
                
                var pointD = TsLabCompressSource.HighPrices.
                    Select((value, index) => new Point {Value = value, Index = index}).
                    Skip(pointC.Index + 1).Take(indexCompressBar - pointC.Index).
                    FirstOrDefault(x => x.Value >= pointB.Value - ScopeDeltaSimple);
                // Входа по Simple еще не было
                if (pointD == null) continue;

                var pointE = TsLabCompressSource.LowPrices.
                    Select((value, index) => new Point {Value = value, Index = index}).
                    Skip(pointD.Index).Take(indexCompressBar - pointD.Index + 1).
                    FirstOrDefault(x => x.Value <= pointB.Value - ScopeStopeSimple);
                // Simple модель не закрылась по стопу
                if (pointE == null) continue;

                // Образовалась сутуация, когда одна пятиминутка касается нескольких уровней (стопа и входа или стопа, входа и профита)
                // Нужен разбор ситуации на уровне пяти секунд
                if (pointD.Index == pointE.Index)
                {
                    var startDate = TsLabCompressSource.Bars[pointE.Index].Date;
                    var endDate = startDate.AddMinutes(4).AddSeconds(55);
                    var decompressRange = TsLabSource.Bars.Where(x => x.Date >= startDate && x.Date <= endDate).ToList();

                    var pointDNotCompress = decompressRange.
                    Select((value, index) => new Point { Value = value.High, Index = index }).
                    FirstOrDefault(x => x.Value >= pointB.Value - ScopeDeltaSimple);

                    var pointENotCompress = decompressRange.
                    Select((value, index) => new Point { Value = value.Low, Index = index }).
                    Skip(pointDNotCompress.Index).
                    FirstOrDefault(x => x.Value <= pointB.Value - ScopeStopeSimple);
                    // Simple модель не закрылась на первом баре
                    if (pointENotCompress == null)
                    {
                        //Найти точку E пропустив первый бар
                        pointE = TsLabCompressSource.LowPrices.
                        Select((value, index) => new Point { Value = value, Index = index }).
                        Skip(pointD.Index + 1).Take(indexCompressBar - pointD.Index).
                        FirstOrDefault(x => x.Value <= pointB.Value - ScopeStopeSimple);
                        // Simple модель не закрылась по стопу
                        if (pointE == null) continue;
                    }
                    else
                    {
                        if (pointDNotCompress.Index == pointENotCompress.Index)
                        {
                            // Нет возможности точно определить что было в первую очередь
                            TsLabContext.Log($"Открытие позиции и пересечение стопа на одном пятисекундном баре, acctualBar = {actualBar}", new Color(), true);
                            continue;
                        }

                        if (decompressRange[pointENotCompress.Index].High >= pointB.Value + ScopeProfiteSimple)
                        {
                            // Нет возможности точно определить что было в первую очередь
                            TsLabContext.Log($"Один пятисекундный бар коснулся и стопа и профита, acctualBar = {actualBar}", new Color(), true);
                            continue;
                        }

                        var validateMaxNotCompress = decompressRange.
                            Skip(pointDNotCompress.Index).Take(pointENotCompress.Index - pointDNotCompress.Index).
                            Max(x => x.High);
                        // Simple модель закрылась в течении первого пятиминутного бара по профиту
                        if (validateMaxNotCompress >= pointB.Value + ScopeProfiteSimple) continue;
                        
                        // Остался только один вариант, Simple модель закрылась в течении первого пятиминутного бара по стопу
                        // В этом случае точка E определена верно и с ней нужно продолжить работу pointD == pointE
                    }
                }

                // Образовалась сутуация, когда одна пятиминутка уже после входа в позицию касается нескольких уровней (стопа и профита)
                // Нужен разбор ситуации на уровне пяти секунд
                if (pointD.Index != pointE.Index && TsLabCompressSource.HighPrices[pointE.Index] >= pointB.Value + ScopeProfiteSimple)
                {
                    var startDate = TsLabCompressSource.Bars[pointE.Index].Date;
                    var endDate = startDate.AddMinutes(4).AddSeconds(55);
                    var decompressRange = TsLabSource.Bars.Where(x => x.Date >= startDate && x.Date <= endDate).ToList();

                    var profitPoint = decompressRange
                        .Select((value, index) => new Point {Value = value.High, Index = index})
                        .First(x => x.Value >= pointB.Value + ScopeProfiteSimple);

                    var stopPoint = decompressRange
                        .Select((value, index) => new Point { Value = value.Low, Index = index })
                        .First(x => x.Value <= pointB.Value - ScopeStopeSimple); 

                    if (profitPoint.Index == stopPoint.Index)
                    {
                        // Нет возможности точно определить что было в первую очередь
                        TsLabContext.Log($"Один пятисекундный бар коснулся и стопа и профита, acctualBar = {actualBar}", new Color(), true);
                        continue; 
                    }

                    // Simple модель закрылась по профиту
                    if (profitPoint.Index < stopPoint.Index) continue;
                }

                if (pointB.Value - TsLabCompressSource.LowPrices[pointE.Index] > 400) continue;
                if (indexCompressBar - pointE.Index > WeitCountBar) continue;

                if (pointE.Index != indexCompressBar)
                {
                    var validateMin = TsLabCompressSource.LowPrices.
                        Skip(pointE.Index + 1).Take(indexCompressBar - pointE.Index).
                        Min();
                    if (pointE.Value >= validateMin) continue;
                }

                modelSellList.Add(pointE.Value);
                Model.SellSignal[actualBar] = 1;
            }

            TsLabContext.StoreObject("SellModel", modelSellList);
        }

        private void SearchBuyModel(int indexCompressBar, int indexBeginDayBar, int actualBar)
        {
            var modelBuyList = new List<double>();

            for (var indexPointA = indexCompressBar - 1;
                indexPointA >= indexBeginDayBar && indexPointA >= 0;
                indexPointA--)
            {
                var pointC = SimpleService.GetHighPrices(TsLabCompressSource.HighPrices, indexPointA, indexCompressBar);
                var pointB = SimpleService.GetLowPrices(TsLabCompressSource.LowPrices, indexPointA, pointC.Index);
                var realPointA = SimpleService.GetHighPrices(TsLabCompressSource.HighPrices, indexPointA, pointB.Index);

                // Точки A и B не могут быть на одном баре
                if (pointB.Index == realPointA.Index) continue;

                // Проверм размер фигуры A-B
                var ab = realPointA.Value - pointB.Value;
                if (ab <= LengthSegmentBC || ab >= LengthSegmentAB) continue;
                
                // Точки B и C не могут быть на одном баре
                if (pointB.Index == pointC.Index) continue;

                // Проверям размер модели B-C
                if (pointC.Value - pointB.Value <= LengthSegmentBC ||
                    realPointA.Value - pointC.Value < 0) continue;

                // Входа по Simple еще не было
                if (indexCompressBar == pointC.Index) continue;

                var pointD = TsLabCompressSource.LowPrices.
                    Select((value, index) => new Point {Value = value, Index = index}).
                    Skip(pointC.Index + 1).Take(indexCompressBar - pointC.Index).
                    FirstOrDefault(x => x.Value <= pointB.Value + ScopeDeltaSimple);
                // Входа по Simple еще не было
                if (pointD == null) continue;

                var pointE = TsLabCompressSource.HighPrices.
                    Select((value, index) => new Point {Value = value, Index = index}).
                    Skip(pointD.Index).Take(indexCompressBar - pointD.Index + 1).
                    FirstOrDefault(x => x.Value >= pointB.Value + ScopeStopeSimple);
                // Simple модель не закрылась по стопу
                if (pointE == null) continue;

                // Образовалась сутуация, когда одна пятиминутка касается нескольких уровней (стопа и входа или стопа, входа и профита)
                // Нужен разбор ситуации на уровне пяти секунд
                if (pointD.Index == pointE.Index)
                {
                    var startDate = TsLabCompressSource.Bars[pointE.Index].Date;
                    var endDate = startDate.AddMinutes(4).AddSeconds(55);
                    var decompressRange =
                        TsLabSource.Bars.Where(x => x.Date >= startDate && x.Date <= endDate).ToList();

                    var pointDNotCompress = decompressRange.
                        Select((value, index) => new Point {Value = value.Low, Index = index}).
                        FirstOrDefault(x => x.Value <= pointB.Value + ScopeDeltaSimple);

                    var pointENotCompress = decompressRange.
                        Select((value, index) => new Point {Value = value.High, Index = index}).
                        Skip(pointDNotCompress.Index).
                        FirstOrDefault(x => x.Value >= pointB.Value + ScopeStopeSimple);
                    // Simple модель не закрылась на первом баре
                    if (pointENotCompress == null)
                    {
                        //Найти точку E пропустив первый бар
                        pointE = TsLabCompressSource.HighPrices.
                            Select((value, index) => new Point {Value = value, Index = index}).
                            Skip(pointD.Index + 1).Take(indexCompressBar - pointD.Index).
                            FirstOrDefault(x => x.Value >= pointB.Value + ScopeStopeSimple);
                        // Simple модель не закрылась по стопу
                        if (pointE == null) continue;
                    }
                    else
                    {
                        if (pointDNotCompress.Index == pointENotCompress.Index)
                        {
                            // Нет возможности точно определить что было в первую очередь
                            TsLabContext.Log(
                                $"Открытие позиции и пересечение стопа на одном пятисекундном баре, acctualBar = {actualBar}",
                                new Color(), true);
                            continue;
                        }

                        if (decompressRange[pointENotCompress.Index].Low >= pointB.Value - ScopeProfiteSimple)
                        {
                            // Нет возможности точно определить что было в первую очередь
                            TsLabContext.Log(
                                $"Один пятисекундный бар коснулся и стопа и профита, acctualBar = {actualBar}",
                                new Color(), true);
                            continue;
                        }

                        var validateMinNotCompress = decompressRange
                            .Skip(pointDNotCompress.Index)
                            .Take(pointENotCompress.Index - pointDNotCompress.Index)
                            .Min(x => x.Low);
                        // Simple модель закрылась в течении первого пятиминутного бара по профиту
                        if (validateMinNotCompress <= pointB.Value - ScopeProfiteSimple) continue;

                        // Остался только один вариант, Simple модель закрылась в течении первого пятиминутного бара по стопу
                        // В этом случае точка E определена верно и с ней нужно продолжить работу pointD == pointE
                    }
                }

                // Образовалась сутуация, когда одна пятиминутка уже после входа в позицию касается нескольких уровней (стопа и профита)
                // Нужен разбор ситуации на уровне пяти секунд
                if (pointD.Index != pointE.Index &&
                    TsLabCompressSource.LowPrices[pointE.Index] <= pointB.Value - ScopeProfiteSimple)
                {
                    var startDate = TsLabCompressSource.Bars[pointE.Index].Date;
                    var endDate = startDate.AddMinutes(4).AddSeconds(55);
                    var decompressRange = TsLabSource.Bars.Where(x => x.Date >= startDate && x.Date <= endDate).ToList();

                    var profitPoint = decompressRange
                        .Select((value, index) => new Point {Value = value.Low, Index = index})
                        .First(x => x.Value <= pointB.Value - ScopeProfiteSimple);

                    var stopPoint = decompressRange
                        .Select((value, index) => new Point {Value = value.High, Index = index})
                        .First(x => x.Value >= pointB.Value + ScopeStopeSimple);

                    if (profitPoint.Index == stopPoint.Index)
                    {
                        // Нет возможности точно определить что было в первую очередь
                        TsLabContext.Log(
                            $"Один пятисекундный бар коснулся и стопа и профита, acctualBar = {actualBar}", new Color(),
                            true);
                        continue;
                    }

                    // Simple модель закрылась по профиту
                    if (profitPoint.Index < stopPoint.Index) continue;
                }

                if (TsLabCompressSource.HighPrices[pointE.Index] - pointB.Value > 400) continue;
                if (indexCompressBar - pointE.Index > WeitCountBar) continue;

                if (pointE.Index != indexCompressBar)
                {
                    var validateMax = TsLabCompressSource.HighPrices.
                        Skip(pointE.Index + 1).Take(indexCompressBar - pointE.Index).
                        Max();
                    if (pointE.Value <= validateMax) continue;
                }

                modelBuyList.Add(pointE.Value);
                Model.BuySignal[actualBar] = 1;
            }

            TsLabContext.StoreObject("BuyModel", modelBuyList);
        }

        private void SetStopToOpenPosition(int actualBar)
        {
            var modelSellList = (List<double>)TsLabContext.LoadObject("SellModel") ?? new List<double>();
            if (modelSellList.Any())
            {
                var sellList = ValidateSellModel(modelSellList, actualBar);
                foreach (double value in sellList)
                {
                    TsLabSource.Positions.SellIfLess(actualBar + 1, Value, value, Slippage, "sell_" + value);
                }
                TsLabContext.StoreObject("SellList", sellList);
            }

            var modelBuyList = (List<double>)TsLabContext.LoadObject("BuyModel") ?? new List<double>();
            if (modelBuyList.Any())
            {
                var buyList = ValidateBuyModel(modelBuyList, actualBar);
                foreach (double value in buyList)
                {
                    TsLabSource.Positions.BuyIfLess(actualBar + 1, Value, value, Slippage, "buy_" + value);
                }
                TsLabContext.StoreObject("BuyList", buyList);
            }
        }

        private List<double> ValidateBuyModel(List<double> modelBuyList, int actualBar)
        {
            double lastMax = double.MinValue;

            for (var i = actualBar; i >= 0 && !SimpleService.IsStartFiveMinutesBar(TsLabSource, i); i--)
            {
                lastMax = TsLabSource.HighPrices[i] > lastMax ? TsLabSource.HighPrices[i] : lastMax;
            }

            return modelBuyList.Where(value => value > lastMax).ToList();
        }

        private List<double> ValidateSellModel(List<double> modelSellList, int actualBar)
        {
            double lastMin = double.MaxValue;

            for (var i = actualBar; i >= 0 && !SimpleService.IsStartFiveMinutesBar(TsLabSource, i); i--)
            {
                lastMin = TsLabSource.LowPrices[i] < lastMin ? TsLabSource.LowPrices[i] : lastMin;
            }

            return modelSellList.Where(value => value < lastMin).ToList();
        }
    }
}