/**
 * Описание:
 * Скрипт для работы на интервале 5 секунд, данные будут сжаты до 5 минут.
 * На иных таймфреймах работать не будет.
 * 
 * Параметры для оптимизации:
 *  - HistorySource - выставить "1" если используются исторические данные
 */

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
        public OptimProperty HistorySource = new OptimProperty(0,0,1,1);

        public virtual void Execute(IContext ctx, ISecurity source)
        {
            // Проверяем таймфрейм входных данных
            if (!GetValidTimeFrame(ctx, source)) return;

            // Генерация графика исходного таймфрейма
            var pain = ctx.CreatePane("Original", 20, false);
            pain.AddList(source.Symbol, source, CandleStyles.BAR_CANDLE, new Color(100, 100, 100), PaneSides.RIGHT);

            // Компрессия исходного таймфрейма в пятиминутный
            var compressSource = source.CompressTo(5);

            // Генерация графика с новым таймфреймом
            var compressPain = ctx.CreatePane("Compress", 20, false);
            var compressChart = compressPain.AddList(source.Symbol, compressSource, CandleStyles.BAR_CANDLE, new Color(100, 100, 100),
                PaneSides.RIGHT);

            if (HistorySource.Value == 1)
            {
                ctx.Log("Исторические данные",new Color(),true);
                for (var historyBar = 1; historyBar <= source.Bars.Count - 1; historyBar++)
                {
                    Trading(ctx, source, compressSource, historyBar, compressChart);
                }
            }
            else
            {
                Trading(ctx, source, compressSource, source.Bars.Count - 1, compressChart);
            }   
        }

        public void Trading(IContext ctx, ISecurity source, ISecurity compressSource, int bar, IGraphList compressChart)
        {
            // Если время 18:40 или более - закрыть все активные позиции и не торговать
            if (source.Bars[bar].Date.TimeOfDay >= new TimeSpan(18, 40, 00))
            {
                if (source.Positions.ActivePositionCount > 0)
                {
                    CloseAllPosition(source, bar);
                }
                return;
            }

            // Посик активных позиций
            SearchActivePosition(source, bar);

            var indexCompressBar = GetIndexCompressBar(bar);
            var indexBeginDayBar = GetIndexBarBeginDay(compressSource, indexCompressBar);

            // Поиск моделей на покупку и выставление для них ордеров
            SearchBuyModel(compressSource, indexCompressBar, indexBeginDayBar);

            // Поиск моделей на продажу и выставление для них ордеров
            //SearchSellModel(compressSource, bar, highPriceList, lowPriceList, compressChart);


        }

        public void SearchBuyModel(ISecurity compressSource, int indexCompressBar, int indexBeginDayBar)
        {
            
            
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

//        public void SearchSellModel(ISecurity source, int bar, List<double> highPriceList, List<double> lowPriceList, IGraphList mainChart)
//        {
//            // Не торгует на вечерней сессии
//            if (source.Bars[bar].Date.TimeOfDay >= new TimeSpan(18, 40, 00)) return;
//
//            var indexBarBeginDay = GetIndexBarBeginDay(source, bar);
//
//            for (var indexPointA = bar - 1; indexPointA >= indexBarBeginDay && indexPointA >= 0; indexPointA--)
//            {
//                // Находим точку B
//                var rangeFindPointB = lowPriceList.GetRange(indexPointA, bar - indexPointA + 1);
//                var lowPricePointB = rangeFindPointB.Min();
//                var indexPointB = indexPointA + rangeFindPointB.IndexOf(lowPricePointB);
//
//                // Корректируем точку A
//                var realRangePointA = highPriceList.GetRange(indexPointA, indexPointB - indexPointA + 1);
//                var realHighPricePointA = realRangePointA.Max();
//                var realIncexPointA = indexPointA + realRangePointA.IndexOf(realHighPricePointA);
//
//                // Проверм размер фигуры A-B
//                var ab = realHighPricePointA - lowPricePointB;
//                if (ab <= 400 || ab >= 1000) continue;
//
//                // Находим точку C
//                var rangeFindPointC = highPriceList.GetRange(indexPointB + 1, bar - indexPointB);
//                if (rangeFindPointC.Count == 0) continue;
//                var highPricePointC = rangeFindPointC.Max();
//                var indexPointC = indexPointB + rangeFindPointC.IndexOf(highPricePointC);
//
//                // Проверям размер модели B-C
//                var bc = highPricePointC - lowPricePointB;
//                var ac = realHighPricePointA - highPricePointC;
//                if (bc <= 389 || ac < 0) continue;
//
//                mainChart.SetColor(realIncexPointA, new Color(255, 0, 0));
//                mainChart.SetColor(indexPointB, new Color(0, 255, 0));
//                mainChart.SetColor(indexPointC, new Color(0, 0, 255));
//
//                if (!(highPriceList.GetRange(indexPointC, bar - indexPointC).Min() <= lowPricePointB + 50))
//                {
//                    source.Positions.SellIfLess(bar + 1, 1, lowPricePointB +50, "sell_" + lowPricePointB);
//                }
//            }
//        }

        public void SearchActivePosition(ISecurity source, int bar)
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
                        position.CloseAtProfit(bar + 1, Convert.ToDouble(arr[1]) + 100, "closeProfit");
                        position.CloseAtStop(bar + 1, Convert.ToDouble(arr[1]) - 300, "closeStop");
                        break;
                    case "sell":
                        position.CloseAtProfit(bar + 1, Convert.ToDouble(arr[1]) - 100, "closeProfit");
                        position.CloseAtStop(bar + 1, Convert.ToDouble(arr[1]) + 300, "closeStop");
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

        public int GetIndexBarBeginDay(ISecurity compressSource, int indexCompressBar)
        {
            return ((int)compressSource.Bars[indexCompressBar].Date.TimeOfDay.TotalMinutes - 600)/5;
        }

        public bool GetValidTimeFrame(IContext ctx, ISecurity source)
        {
            if (source.IntervalBase == DataIntervals.SECONDS && source.Interval == 5) return true;
            ctx.Log("Выбран не верный таймфрейм, выберите таймфрейм равный 5 секундам",new Color(255,0,0),true);
            return false;
        }

        public int GetIndexCompressBar(int bar)
        {
            var tempbar = bar;
            while (tempbar % 60 != 0)
            {
                tempbar--;
            }
            return tempbar / 60;
        }
    }
}
