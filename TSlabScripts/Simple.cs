using System;
using System.Linq;
using System.Collections.Generic;
using TSLab.DataSource;
using TSLab.Script;
using TSLab.Script.Handlers;
using TSLab.Script.Optimization;

namespace TSLabScripts
{
    public class Simple : IExternalScript
    {
        //public OptimProperty HistoryStartBar = new OptimProperty(1,0,);

        public virtual void Execute(IContext ctx, ISecurity source)
        {
            // Генерация графика
            var mainPain = ctx.CreatePane("Simple", 20, false);
            var mainChart = mainPain.AddList(source.Symbol, source, CandleStyles.BAR_CANDLE, new Color(100, 100, 100),
                PaneSides.RIGHT);

            // Не торгует на интервалах - День и Тик
            if (source.IntervalBase == DataIntervals.DAYS || source.IntervalBase == DataIntervals.TICK) return;

            //Конвертация значений
            var highPriceList = source.HighPrices.ToList();
            var lowPriceList = source.LowPrices.ToList();

            //SearchPosition(source, source.Bars.Count);
            //SearchBuyModel(source, source.Bars.Count - 1, highPriceList, lowPriceList, mainChart);
            //SearchSellModel(source, source.Bars.Count - 1, highPriceList, lowPriceList, mainChart);

            for (var historyBar = 1; historyBar <= source.Bars.Count - 1; historyBar++)
            {
                SearchPosition(source, historyBar + 1);
                SearchBuyModel(source, historyBar, highPriceList, lowPriceList, mainChart);
                SearchSellModel(source, historyBar, highPriceList, lowPriceList, mainChart);
            }
        }

        public void SearchBuyModel(ISecurity source, int bar, List<double> highPriceList, List<double> lowPriceList, IGraphList mainChart)
        {
            // Не торгует на вечерней сессии
            if (source.Bars[bar].Date.TimeOfDay >= new TimeSpan(18, 40, 00))
            {
                CloseAllPosition(source, bar + 1);
                return;
            }

            var indexBarBeginDay = GetIndexBarBeginDay(source, bar);
            
            for (var indexPointA = bar - 1; indexPointA >= indexBarBeginDay && indexPointA >= 0; indexPointA--)
            {
                // Находим точку B
                var rangeFindPointB = highPriceList.GetRange(indexPointA, bar - indexPointA + 1);
                var highPricePointB = rangeFindPointB.Max();
                var indexPointB = indexPointA + rangeFindPointB.IndexOf(highPricePointB);

                // Корректируем точку A
                var realRangePointA = lowPriceList.GetRange(indexPointA, indexPointB - indexPointA + 1);
                var realLowPricePointA = realRangePointA.Min();
                var realIncexPointA = indexPointA + realRangePointA.IndexOf(realLowPricePointA);

                // Проверм размер фигуры A-B
                var ab = highPricePointB - realLowPricePointA;
                if (ab <= 400 || ab >= 1000) continue;

                // Находим точку C
                var rangeFindPointC = lowPriceList.GetRange(indexPointB + 1, bar - indexPointB);
                if (rangeFindPointC.Count == 0) continue;
                var lowPricePointC = rangeFindPointC.Min();
                var indexPointC = indexPointB + rangeFindPointC.IndexOf(lowPricePointC);

                // Проверям размер модели B-C
                var bc = highPricePointB - lowPricePointC;
                var ac = lowPricePointC - realLowPricePointA;
                if (bc <= 389 || ac < 0) continue;

                mainChart.SetColor(realIncexPointA, new Color(255,0,0));
                mainChart.SetColor(indexPointB, new Color(0,255,0));
                mainChart.SetColor(indexPointC, new Color(0,0,255));

                if (!(highPriceList.GetRange(indexPointC, bar - indexPointC).Max() >= highPricePointB - 50))
                {
                    source.Positions.BuyIfGreater(bar + 1, 1, highPricePointB - 50, "buy_" + highPricePointB);
                }
            }   
        }

        public void SearchSellModel(ISecurity source, int bar, List<double> highPriceList, List<double> lowPriceList, IGraphList mainChart)
        {
            // Не торгует на вечерней сессии
            if (source.Bars[bar].Date.TimeOfDay >= new TimeSpan(18, 40, 00)) return;

            var indexBarBeginDay = GetIndexBarBeginDay(source, bar);

            for (var indexPointA = bar - 1; indexPointA >= indexBarBeginDay && indexPointA >= 0; indexPointA--)
            {
                // Находим точку B
                var rangeFindPointB = lowPriceList.GetRange(indexPointA, bar - indexPointA + 1);
                var lowPricePointB = rangeFindPointB.Min();
                var indexPointB = indexPointA + rangeFindPointB.IndexOf(lowPricePointB);

                // Корректируем точку A
                var realRangePointA = highPriceList.GetRange(indexPointA, indexPointB - indexPointA + 1);
                var realHighPricePointA = realRangePointA.Max();
                var realIncexPointA = indexPointA + realRangePointA.IndexOf(realHighPricePointA);

                // Проверм размер фигуры A-B
                var ab = realHighPricePointA - lowPricePointB;
                if (ab <= 400 || ab >= 1000) continue;

                // Находим точку C
                var rangeFindPointC = highPriceList.GetRange(indexPointB + 1, bar - indexPointB);
                if (rangeFindPointC.Count == 0) continue;
                var highPricePointC = rangeFindPointC.Max();
                var indexPointC = indexPointB + rangeFindPointC.IndexOf(highPricePointC);

                // Проверям размер модели B-C
                var bc = highPricePointC - lowPricePointB;
                var ac = realHighPricePointA - highPricePointC;
                if (bc <= 389 || ac < 0) continue;

                mainChart.SetColor(realIncexPointA, new Color(255, 0, 0));
                mainChart.SetColor(indexPointB, new Color(0, 255, 0));
                mainChart.SetColor(indexPointC, new Color(0, 0, 255));

                if (!(highPriceList.GetRange(indexPointC, bar - indexPointC).Min() <= lowPricePointB + 50))
                {
                    source.Positions.SellIfLess(bar + 1, 1, lowPricePointB +50, "sell_" + lowPricePointB);
                }
            }
        }

        public void SearchPosition(ISecurity source, int bar)
        {

            var positionList = from position in source.Positions
                               where position.IsActive == true
                               select position;

            foreach (var position in positionList)
            {
                var arr = position.EntrySignalName.Split('_');

                switch (arr[0])
                {
                    case "buy":
                        position.CloseAtProfit(bar, Convert.ToDouble(arr[1]) + 100, "closeProfit");
                        position.CloseAtStop(bar, Convert.ToDouble(arr[1]) - 300, "closeStop");
                        break;
                    case "sell":
                        position.CloseAtProfit(bar, Convert.ToDouble(arr[1]) - 100, "closeProfit");
                        position.CloseAtStop(bar, Convert.ToDouble(arr[1]) + 300, "closeStop");
                        break;
                }
            }
        }

        private void CloseAllPosition(ISecurity source, int bar)
        {
            var positionList = from position in source.Positions
                where position.IsActive == true
                select position;

            foreach (var position in positionList)
            {
                position.CloseAtMarket(bar + 1, "closeAtTime");
            }
        }

        public int GetIndexBarBeginDay(ISecurity source, int historyBar)
        {
            var timeBeginDay = new TimeSpan(10, 00, 00);
            var timeСurrentBar = source.Bars[historyBar].Date.TimeOfDay;
            int countBar;

            if (source.IntervalBase == DataIntervals.MINUTE)
            {
                countBar = Convert.ToInt32((timeСurrentBar.TotalMinutes - timeBeginDay.TotalMinutes) / source.Interval);
                return countBar <= 0 ? 0 : historyBar - countBar;
            }

            countBar = Convert.ToInt32((timeСurrentBar.TotalSeconds - timeBeginDay.TotalSeconds) / source.Interval);
            return countBar <= 0 ? 0 : historyBar - countBar;
        }
    }
}
