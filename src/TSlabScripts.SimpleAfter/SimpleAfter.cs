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
        public OptimProperty LengthSegmentAB = new OptimProperty(1000, double.MinValue, double.MaxValue, 0.01);
        public OptimProperty LengthSegmentBC = new OptimProperty(390, double.MinValue, double.MaxValue, 0.01);
        public OptimProperty ScopeDeltaSimple = new OptimProperty(50, double.MinValue, double.MaxValue, 0.01);
        public OptimProperty ScopeProfiteSimple = new OptimProperty(100, double.MinValue, double.MaxValue, 0.01);
        public OptimProperty ScopeStopeSimple = new OptimProperty(300, double.MinValue, double.MaxValue, 0.01);

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
                var indexBeginDayBar = SimpleService.GetIndexBeginDayBar(TsLabSource, dateActualBar);
                var indexCompressBar = SimpleService.GetIndexActualCompressBar(TsLabCompressSource, dateActualBar, indexBeginDayBar);

                SearchBuyModel(indexCompressBar, indexBeginDayBar, actualBar);
                SearchSellModel(indexCompressBar, indexBeginDayBar, actualBar);
            }

            SetStopToOpenPosition(actualBar);
        }

        private void SetStopForActivePosition(int actualBar)
        {

        }

        private void SearchBuyModel(int indexCompressBar, int indexBeginDayBar, int actualBar)
        {
            var modelBuyList = new List<double>();

            for (var indexPointA = indexCompressBar - 1; indexPointA >= indexBeginDayBar && indexPointA >= 0; indexPointA--)
            {
                var pointB = SimpleService.GetHighPrices(TsLabCompressSource.HighPrices, indexPointA, indexCompressBar);
                var realPointA = SimpleService.GetLowPrices(TsLabCompressSource.LowPrices, indexPointA, pointB.Index);

                // Точки A и B не могут быть на одном баре
                if (pointB.Index == realPointA.Index) continue;

                // Проверм размер фигуры A-B
                var ab = pointB.Value - realPointA.Value;
                if (ab <= LengthSegmentBC || ab >= LengthSegmentAB) continue;

                var pointC = SimpleService.GetLowPrices(TsLabCompressSource.LowPrices, pointB.Index, indexCompressBar);

                // Точки B и C не могут быть на одном баре
                if (pointB.Index == pointC.Index) continue;

                // Проверям размер модели B-C
                if (pointB.Value - pointC.Value <= LengthSegmentBC ||
                    pointC.Value - realPointA.Value < 0) continue;

                // Проверка на пересечение
                if (indexCompressBar != pointC.Index)
                {
                    var validateMax = TsLabCompressSource.HighPrices.
                        Skip(pointC.Index + 1).
                        Take(indexCompressBar - pointC.Index).
                        Max();
                    if (pointB.Value - ScopeDeltaSimple <= validateMax) continue;
                }

                modelBuyList.Add(pointB.Value);

                Model.BuySignal[actualBar] = 1;
            }

            TsLabContext.StoreObject("BuyModel", modelBuyList);
        }

        private void SearchSellModel(int indexCompressBar, int indexBeginDayBar, int actualBar)
        {
            var modelSellList = new List<double>();

            for (var indexPointA = indexCompressBar - 1; indexPointA >= indexBeginDayBar && indexPointA >= 0; indexPointA--)
            {
                var pointB = TsLabCompressSource.LowPrices.
                    Select((value, index) => new { Value = value, Index = index }).
                    Skip(indexPointA).
                    Take(indexCompressBar - indexPointA + 1).
                    OrderBy(x => x.Value).First();

                var realPointA = TsLabCompressSource.HighPrices.
                    Select((value, index) => new { Value = value, Index = index }).
                    Skip(indexPointA).
                    Take(pointB.Index - indexPointA + 1).
                    OrderBy(x => x.Value).Last();

                // Точки A и B не могут быть на одном баре
                if (pointB.Index == realPointA.Index) continue;

                // Проверм размер фигуры A-B
                var ab = realPointA.Value - pointB.Value;
                if (ab <= LengthSegmentBC || ab >= LengthSegmentAB) continue;

                var pointC = TsLabCompressSource.HighPrices.
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
                    var validateMin = TsLabCompressSource.LowPrices.
                        Skip(pointC.Index + 1).
                        Take(indexCompressBar - pointC.Index).
                        Min();
                    if (pointB.Value + ScopeDeltaSimple >= validateMin) continue;
                }

                modelSellList.Add(pointB.Value);

                Model.SellSignal[actualBar] = 1;
            }

            TsLabContext.StoreObject("SellModel", modelSellList);
        }

        private void SetStopToOpenPosition(int actualBar)
        {

        }

        private List<double> ValidateBuyModel(List<double> modelBuyList, int actualBar)
        {
            return new List<double>();
        }

        private List<double> ValidateSellModel(List<double> modelSellList, int actualBar)
        {
            return new List<double>();
        }
    }
}