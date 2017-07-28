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

            // ��������� ��������� ������� ������
            if (!SimpleService.GetValidTimeFrame(TsLabSource.IntervalBase, TsLabSource.Interval))
            {
                TsLabContext.Log("������ �� ������ ���������, �������� ��������� ������ 5 ��������", new Color(255, 0, 0), true);
                return;
            }

            // ���������� ��������� ���������� � ������������
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
            // ���� ����� ����� 10:00 - �� ���������
            if (TsLabSource.Bars[actualBar].Date.TimeOfDay < TimeBeginBar) return;

            // ���� ����� 18:40 ��� ����� - ������� ��� �������� ������� � �� ���������
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
                //SearchBuyModel(indexCompressBar, indexBeginDayBar, actualBar);
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
//                    case "buy":
//                        position.CloseAtProfit(actualBar + 1, Convert.ToDouble(arr[1]) + ScopeProfite, "closeProfit");
//                        position.CloseAtStop(actualBar + 1, Convert.ToDouble(arr[1]) - ScopeStope, Slippage, "closeStop");
//                        break;
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
                var pointB = SimpleService.GetHighPrices(TsLabCompressSource.HighPrices, indexPointA, indexCompressBar);
                var realPointA = SimpleService.GetLowPrices(TsLabCompressSource.LowPrices, indexPointA, pointB.Index);

                // ����� A � B �� ����� ���� �� ����� ����
                if (pointB.Index == realPointA.Index) continue;

                // ������� ������ ������ A-B
                var ab = pointB.Value - realPointA.Value;
                if (ab <= LengthSegmentBC || ab >= LengthSegmentAB) continue;

                var pointC = SimpleService.GetLowPrices(TsLabCompressSource.LowPrices, pointB.Index, indexCompressBar);

                // ����� B � C �� ����� ���� �� ����� ����
                if (pointB.Index == pointC.Index) continue;

                // �������� ������ ������ B-C
                if (pointB.Value - pointC.Value <= LengthSegmentBC ||
                    pointC.Value - realPointA.Value < 0) continue;

                // �������� �� �����������-
                if (indexCompressBar == pointC.Index) continue;
                
                var pointD = TsLabCompressSource.HighPrices.
                    Select((value, index) => new Point {Value = value, Index = index}).
                    Skip(pointC.Index + 1).Take(indexCompressBar - pointC.Index).
                    FirstOrDefault(x => x.Value >= pointB.Value - ScopeDeltaSimple);
                if (pointD == null) continue;

                var pointE = TsLabCompressSource.LowPrices.
                    Select((value, index) => new Point {Value = value, Index = index}).
                    Skip(pointD.Index).Take(indexCompressBar - pointD.Index + 1).
                    FirstOrDefault(x => x.Value <= pointB.Value - ScopeStopeSimple);
                if (pointE == null) continue;
                if (pointD.Index == pointE.Index)
                {
                    var startDate = TsLabCompressSource.Bars[pointE.Index].Date;
                    var endDate = startDate.AddMinutes(4).AddSeconds(55);
                    var range = TsLabSource.Bars.Where(x => x.Date >= startDate && x.Date <= endDate).ToList();

                    var pointDNotCompress = range.
                    Select((value, index) => new Point { Value = value.High, Index = index }).
                    FirstOrDefault(x => x.Value >= pointB.Value - ScopeDeltaSimple);
                    if (pointDNotCompress == null) continue;

                    var pointENotCompress = range.
                    Select((value, index) => new Point { Value = value.Low, Index = index }).
                    Skip(pointDNotCompress.Index).
                    FirstOrDefault(x => x.Value >= pointB.Value - ScopeDeltaSimple);
                    if (pointENotCompress == null) continue;

                    if (pointDNotCompress.Index == pointENotCompress.Index)
                    {
                        TsLabContext.Log($"pointDNotCompress.Index == pointENotCompress.Index, acctualBar = {actualBar}", new Color(), true);
                        continue;
                    }

                    var validateMaxNotCompress = range.
                        Skip(pointDNotCompress.Index).Take(pointENotCompress.Index - pointDNotCompress.Index + 1).
                        Max(x => x.High);




                        // ���������������� �� ������ 5 ������
                        TsLabContext.Log($"pointD.Index == pointE.Index, acctualBar = {actualBar}",new Color());

                    pointE = TsLabCompressSource.LowPrices.
                    Select((value, index) => new Point { Value = value, Index = index }).
                    Skip(pointD.Index + 1).Take(indexCompressBar - pointD.Index).
                    FirstOrDefault(x => x.Value <= pointB.Value - ScopeStopeSimple);
                    if (pointE == null) continue;
                }
                if (TsLabCompressSource.HighPrices[pointE.Index] >= pointB.Value + ScopeProfiteSimple)
                {
                    // ���������������� �� ������ 5 ������
                    TsLabContext.Log($"TsLabCompressSource.HighPrices[pointE.Index] >= pointB.Value + ScopeProfiteSimple, acctualBar = {actualBar}", new Color());
                    continue;
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
                TsLabContext.Log($"������ �������", new Color());
            }

            TsLabContext.StoreObject("SellModel", modelSellList);
        }

//        private void SearchBuyModel(int indexCompressBar, int indexBeginDayBar, int actualBar)
//        {
//            var modelSellList = new List<double>();
//
//            for (var indexPointA = indexCompressBar - 1; indexPointA >= indexBeginDayBar && indexPointA >= 0; indexPointA--)
//            {
//                var pointB = TsLabCompressSource.LowPrices.
//                    Select((value, index) => new { Value = value, Index = index }).
//                    Skip(indexPointA).
//                    Take(indexCompressBar - indexPointA + 1).
//                    OrderBy(x => x.Value).First();
//
//                var realPointA = TsLabCompressSource.HighPrices.
//                    Select((value, index) => new { Value = value, Index = index }).
//                    Skip(indexPointA).
//                    Take(pointB.Index - indexPointA + 1).
//                    OrderBy(x => x.Value).Last();
//
//                // ����� A � B �� ����� ���� �� ����� ����
//                if (pointB.Index == realPointA.Index) continue;
//
//                // ������� ������ ������ A-B
//                var ab = realPointA.Value - pointB.Value;
//                if (ab <= LengthSegmentBC || ab >= LengthSegmentAB) continue;
//
//                var pointC = TsLabCompressSource.HighPrices.
//                    Select((value, index) => new { Value = value, Index = index }).
//                    Skip(pointB.Index).
//                    Take(indexCompressBar - pointB.Index + 1).
//                    OrderBy(x => x.Value).Last();
//
//                // ����� B � C �� ����� ���� �� ����� ����
//                if (pointB.Index == pointC.Index) continue;
//
//                // �������� ������ ������ B-C
//                if (pointC.Value - pointB.Value <= LengthSegmentBC ||
//                    realPointA.Value - pointC.Value < 0) continue;
//
//                // �������� �� �����������
//                if (indexCompressBar != pointC.Index)
//                {
//                    var validateMin = TsLabCompressSource.LowPrices.
//                        Skip(pointC.Index + 1).
//                        Take(indexCompressBar - pointC.Index).
//                        Min();
//                    if (pointB.Value + ScopeDeltaSimple >= validateMin) continue;
//                }
//
//                modelSellList.Add(pointB.Value);
//
//                Model.SellSignal[actualBar] = 1;
//            }
//
//            TsLabContext.StoreObject("SellModel", modelSellList);
//        }

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
        }

        private List<double> ValidateBuyModel(List<double> modelBuyList, int actualBar)
        {
            return new List<double>();
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