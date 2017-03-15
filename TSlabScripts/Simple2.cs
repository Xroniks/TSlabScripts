using System;
using System.Linq;
using TSLab.DataSource;
using TSLab.Script;
using TSLab.Script.Handlers;
using TSLab.Script.Optimization;

namespace TSLabScripts
{
    public class Simple2 : IExternalScript
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

            // Цвета для раскраски баров
            var red = new Color(255, 0, 0);
            var green = new Color(0, 255, 0);
            var blue = new Color(0, 0, 255);

            //Конвертация значений
            var highPriceList = source.HighPrices.ToList();
            var lowPriceList = source.LowPrices.ToList();

            var totalCount = 0;

            // Просмотр истории - для тестов
            for (var historyBar = 1; historyBar < source.Bars.Count - 1; historyBar++)
            {
                var count = 0;

                // Не торгует на вечерней сессии
                if (source.Bars[historyBar].Date.TimeOfDay > new TimeSpan(18, 35, 00)) return;

                var indexBarBeginDay = GetIndexBarBeginDay(source, historyBar);

                for (var indexPointC = historyBar; indexPointC >= indexBarBeginDay + 1; indexPointC--)
                {
                    totalCount++;
                    count++;

                    var lowPricePointC = lowPriceList[indexPointC];

                    for (var indexPointA = indexPointC - 1; indexPointA >= indexBarBeginDay; indexPointA--)
                    {
                        if (lowPriceList[indexPointA] > lowPricePointC) continue;

                        var rangeFindPointB = highPriceList.GetRange(indexPointA, indexPointA - indexPointC + 2);
                        var highPricePointB = rangeFindPointB.Max();

                        if (highPricePointB - lowPriceList[indexPointA] <= 400) continue;

                        if (highPricePointB - lowPriceList[indexPointA] >= 1000) break;

                        if (highPricePointB - lowPricePointC <= 350) break;

                        var indexPointB = indexPointA + rangeFindPointB.IndexOf(highPricePointB);

                        if (highPriceList.GetRange(indexPointC, historyBar - indexPointC + 1).Max() >= highPricePointB - 50)
                        {
                            if (source.Positions.ActivePositionCount > 0)
                            {
                                var position = source.Positions.GetLastActiveForSignal("buy " + source.Bars[indexPointB].Date);
                                if (position != null)
                                {
                                    position.CloseAtProfit(historyBar + 1, highPricePointB + 100, "closeProfit" + source.Bars[indexPointB].Date);
                                    position.CloseAtStop(historyBar + 1, highPricePointB - 300, "closeStop" + source.Bars[indexPointB].Date);
                                }
                            }
                            break;
                        }

                        source.Positions.BuyIfGreater(historyBar + 1, 1, highPricePointB - 50, "buy " + source.Bars[indexPointB].Date);
                        break;
                    }

                }
                ctx.Log("totalCount " + count + ", historyBar" + historyBar + ", count" + count, new Color(255, 0, 0), true);
            }

        }

        /// <summary>
        /// Возвращает index бара начала дня
        /// </summary>
        /// <param name="source"></param>
        /// <param name="historyBar"></param>
        /// <returns></returns>
        public int GetIndexBarBeginDay(ISecurity source, int historyBar)
        {
            var timeBeginDay = new TimeSpan(10, 00, 00);
            var timeСurrentBar = source.Bars[historyBar].Date.TimeOfDay;
            int countBar;

            if (source.IntervalBase == DataIntervals.MINUTE)
            {
                countBar = Convert.ToInt32((timeСurrentBar.TotalMinutes - timeBeginDay.TotalMinutes) / source.Interval);
                return historyBar - countBar <= 0 ? 0 : historyBar - countBar;
            }

            countBar = Convert.ToInt32((timeСurrentBar.TotalSeconds - timeBeginDay.TotalSeconds) / source.Interval);
            return historyBar - countBar <= 0 ? 0 : historyBar - countBar;
        }
    }
}
