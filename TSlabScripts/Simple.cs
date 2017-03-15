using System;
using System.Linq;
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

                for (var indexStartBar = historyBar; indexStartBar >= indexBarBeginDay + 1 && indexStartBar >= 1; indexStartBar--)
                {
                    for (var indexPointA = indexStartBar - 1; indexPointA >= indexBarBeginDay && indexPointA >= 0; indexPointA--)
                    {
                        totalCount++;
                        count++;
                        // Находим точку B
                        var rangeFindPointB = highPriceList.GetRange(indexPointA, indexStartBar - indexPointA + 1);
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
                        var rangeFindPointC = lowPriceList.GetRange(indexPointB + 1, indexStartBar - indexPointB);
                        if (rangeFindPointC.Count == 0) continue;
                        var lowPricePointC = rangeFindPointC.Min();
                        var indexPointC = indexPointB + rangeFindPointC.IndexOf(lowPricePointC);

                        // Проверям размер модели B-C
                        var bc = highPricePointB - lowPricePointC;
                        var ac = lowPricePointC - realLowPricePointA;
                        if (bc <= 389 || ac < 0) continue;

                        mainChart.SetColor(realIncexPointA, red);
                        mainChart.SetColor(indexPointB, green);
                        mainChart.SetColor(indexPointC, blue);

                        // Выставляем ордера
                        if (highPriceList.GetRange(indexPointC, historyBar - indexPointC).Max() >= highPricePointB - 50)
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
                            continue;
                        }

                        source.Positions.BuyIfGreater(historyBar + 1, 1, highPricePointB - 50, "buy " + source.Bars[indexPointB].Date);
                    }
                }
                ctx.Log("totalCount " + totalCount + ", historyBar" + historyBar + ", count" + count, new Color(255, 0, 0), true);
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
                return countBar <= 0 ? 0 : historyBar - countBar;
            }

            countBar = Convert.ToInt32((timeСurrentBar.TotalSeconds - timeBeginDay.TotalSeconds) / source.Interval);
            return countBar <= 0 ? 0 : historyBar - countBar;
        }
    }
}
