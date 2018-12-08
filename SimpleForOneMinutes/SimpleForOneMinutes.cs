/*
 * Торговая стратегия "Simple". Стратегия расчитана на пробитие уровней экстремума.
 * Для определения точки входа необходимо найти фигру, являющуюся треугольником, с вершинами А, В, С.
 * Точка В должны быть между А и С и быть экстремумом модели. А уровень цены точки С должен быть ближе к уроню цены
 * точки В чем уровень цены точки А.
 * Вход в позицию должен происходить по уровню экстремума - "ScopeDelta". Вход должен произойти чуть раньше точки
 * экстремума, с целью снижения расходов на проскальзывание.
 * Закрытие позиции должно происходить при одном из трех собыйтий:
 *  - цена откатилась на значение больше чем "ScopeStop" (позиция закрывается в убыток)
 *  - цена двинулась в ожидаемом направлении на значение более чем "ScopeProfit" (позиция закрывается в прибыль)
 *  - цена до конца торгового дня остается в коридоре между "ScopeStop" и "ScopeProfit", позиция закрывается по времени
 *     (в 18:40). Можно ускорить выход, воспользовавшись настройкой "DeltaPositionSpan"
 *
 * Для настройки стратегии используются параметры:
 *  - Value, объем позиции, используется для выставления ордеров
 *  - Slippage, проскальзывание, используется для выставления ордеров
 *  - ScopeDelta, размер отступа от уровня экстремума, используется для выставления ордеров
 *  - ScopeStop, размер стопа, задаваемый от уровня цены точки В
 *  - ScopeProfit, размер профита, задаваемый от уровня цены точки В
 *
 *  - LengthSegmentAB, ограничивает разницу между уровнями цен точек А и В. Все модели у которых разница будет БОЛЬШЕ установленной, будут пропускаться
 *  - LengthSegmentBC, ограничивает разницу между уровнями цен точек В и С. Все модели у которых разница будет МЕНЬШЕ установленной, будут пропускаться
 *  - DistanceFromCrossing, ограничивает насколько близко может приближаться цена на отрезках АВ и ВС к уровню цены точки В. Значение -1, отключает ограничение
 *
 *  - DeltaModelSpan, ограничивает время ожидания ВХОДА в позицию. При значении "0" ограничение не работает. Ограничение задается в минутах.
 *  - DeltaPositionSpan, ограничивает время ожидания ВЫХОДА из позиции. При значении "0" ограничение не работает. Ограничение задается в минутах.
*/

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
    public class SimpleForOneMinutes : IExternalScript
    {
        public static OptimProperty Value = new OptimProperty(1, 0, double.MaxValue, 1);
        public static OptimProperty Slippage = new OptimProperty(30, 0, double.MaxValue, 0.01);
        public static OptimProperty ScopeDelta = new OptimProperty(50, 0, double.MaxValue, 0.01);
        public static OptimProperty ScopeProfit = new OptimProperty(100, 0, double.MaxValue, 0.01);
        public static OptimProperty ScopeStop = new OptimProperty(300, 0, double.MaxValue, 0.01);
        
        public static OptimProperty LengthSegmentAB = new OptimProperty(1000, 0, double.MaxValue, 0.01);
        public static OptimProperty LengthSegmentBC = new OptimProperty(390, 0, double.MaxValue, 0.01);
        public static OptimProperty DistanceFromCrossing = new OptimProperty(0, -1, double.MaxValue, 0.01);
        
        public static OptimProperty DeltaModelSpan = new OptimProperty(120, double.MinValue, double.MaxValue, 1);
        public static OptimProperty DeltaPositionSpan = new OptimProperty(120, double.MinValue, double.MaxValue, 1);

        private readonly TimeSpan TimeCloseAllPosition = new TimeSpan(18, 40, 00);
        private readonly TimeSpan TimeBeginDayBar = new TimeSpan(10, 00, 00);
        private readonly TimeSpan TimeBeginBar = new TimeSpan(10, 00, 55);
        private readonly TimeSpan FiveSeconds = new TimeSpan(0, 0, 5);
        private readonly TimeSpan OneMinutes = new TimeSpan(0, 1, 0);
        private readonly TimeSpan DeltaModelTimeSpan = new TimeSpan(0, DeltaModelSpan, 0);
        private readonly TimeSpan DeltaPositionTimeSpan = new TimeSpan(0, DeltaPositionSpan, 0);

        public virtual void Execute(IContext ctx, ISecurity source)
        {
            // Проверяем таймфрейм входных данных
            if (!GetValidTimeFrame(ctx, source)) return;

            // Компрессия исходного таймфрейма в пятиминутный
            var compressSource = source.CompressTo(new Interval(1, DataIntervals.MINUTE), 0, 200, 0);

            // Генерация графика исходного таймфрейма
            var pain = ctx.CreatePane("Original", 70, false);
            pain.AddList(source.Symbol, compressSource, CandleStyles.BAR_CANDLE, new Color(100, 100, 100), PaneSides.RIGHT);
            pain.AddList(source.Symbol, source, CandleStyles.BAR_CANDLE, new Color(0, 0, 0), PaneSides.RIGHT);

            // Инцализировать индикатор моделей пустыми значениями
            var buySignal = Enumerable.Repeat((double)0, source.Bars.Count).ToList();
            var sellSignal = Enumerable.Repeat((double)0, source.Bars.Count).ToList();

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

        private void Trading(IContext ctx, ISecurity source, ISecurity compressSource, int actualBar, List<double> buySignal, List<double> sellSignal)
        {
            // При тестировании на исторических данных появляются бары из предторговой сессии, их необходимо отсекать
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

            // Посик активных позиций и работа с ними
            if (source.Positions.ActivePositionCount > 0)
            {
                SearchActivePosition(source, actualBar);
            }

            if (IsClosedBar(source.Bars[actualBar]))
            {
                var dateActualBar = source.Bars[actualBar].Date;
                var indexBeginDayBar = GetIndexBeginDayBar(compressSource, dateActualBar);
                var indexCompressBar = GetIndexCompressBar(compressSource, dateActualBar, indexBeginDayBar);

                // Поиск моделей, информация о моделях пишется в "StoreObject"
                // Модели ищутся только на открытии бара, а валидируются каждые 5 секунд
                SearchBuyModel(ctx, compressSource, indexCompressBar, indexBeginDayBar, actualBar, buySignal);
                SearchSellModel(ctx, compressSource, indexCompressBar, indexBeginDayBar, actualBar, sellSignal);
            }

            var modelBuyList = (List<TradingModel>)ctx.LoadObject("BuyModel") ?? new List<TradingModel>();
            if (modelBuyList.Any())
            {
                var buyList = ValidateBuyModel(source, modelBuyList, actualBar);
                foreach (var model in buyList)
                {
                    source.Positions.BuyIfGreater(actualBar + 1, Value, model.Value - ScopeDelta, Slippage, "buy_" + model.Value);
                }
                ctx.StoreObject("BuyModel", buyList);
            }

            var modelSellList = (List<TradingModel>)ctx.LoadObject("SellModel") ?? new List<TradingModel>();
            if (modelSellList.Any())
            {
                var sellList = ValidateSellModel(source, modelSellList, actualBar);
                foreach (var model in sellList)
                {
                    source.Positions.SellIfLess(actualBar + 1, Value, model.Value + ScopeDelta, Slippage, "sell_" + model.Value);
                }
                ctx.StoreObject("SellList", sellList);
            }
        }

        private void SearchBuyModel(IContext ctx, ISecurity compressSource, int indexCompressBar, int indexBeginDayBar, int actualBar, List<double> buySignal)
        {
            var modelBuyList = new List<TradingModel>();

            for (var indexPointA = indexCompressBar - 1; indexPointA >= indexBeginDayBar && indexPointA >= 0; indexPointA--)
            {
                var pointB = compressSource.HighPrices
                    .Select((value, index) => new { Value = value, Index = index })
                    .Skip(indexPointA)
                    .Take(indexCompressBar - indexPointA + 1)
                    .MaxBy(item => item.Value);

                var realPointA = compressSource.LowPrices.
                    Select((value, index) => new { Value = value, Index = index }).
                    Skip(indexPointA).
                    Take(pointB.Index - indexPointA + 1).
                    MinBy(item => item.Value);

                // Точки A и B не могут быть на одном баре
                if (pointB.Index == realPointA.Index) continue;

                // Проверм размер фигуры A-B
                var ab = pointB.Value - realPointA.Value;
                if (ab <= LengthSegmentBC || ab >= LengthSegmentAB) continue;
                
                // Проверяем приближение HighPrices на отрезке АВ к уровню точки В
                if(DistanceFromCrossing != -1 
                   && compressSource.HighPrices
                       .Skip(realPointA.Index + 2)
                       .Take(pointB.Index - realPointA.Index)
                       .Max() >= pointB.Value - DistanceFromCrossing) continue;

                var pointC = compressSource.LowPrices.
                    Select((value, index) => new { Value = value, Index = index }).
                    Skip(pointB.Index).
                    Take(indexCompressBar - pointB.Index + 1).
                    MinBy(item => item.Value);

                // Точки B и C не могут быть на одном баре
                if (pointB.Index == pointC.Index) continue;

                // Проверям размер модели B-C
                if (pointB.Value - pointC.Value <= LengthSegmentBC ||
                    pointC.Value - realPointA.Value < 0) continue;
                
                // Проверяем приближение HighPrices на отрезке АС к уровню точки В
                if(DistanceFromCrossing != -1 
                   && compressSource.HighPrices
                       .Skip(pointB.Index + 2)
                       .Take(pointC.Index - pointB.Index)
                       .Max() >= pointB.Value - DistanceFromCrossing) continue;

                // Проверяем, не отработала ли уже модель
                if (indexCompressBar != pointC.Index)
                { 
                    var validateMax = compressSource.HighPrices.
                        Skip(pointC.Index + 1).
                        Take(indexCompressBar - pointC.Index).
                        Max();
                    if (pointB.Value - ScopeDelta <= validateMax) continue;
                }

                // Проверка на время модели
                if (DeltaModelTimeSpan.TotalMinutes > 0 &&
                    compressSource.Bars[indexCompressBar].Date - compressSource.Bars[pointB.Index].Date > DeltaModelTimeSpan)
                    continue;

                modelBuyList.Add(new TradingModel
                {
                    Value = pointB.Value
                });

                buySignal[actualBar] = 1;
            }

            ctx.StoreObject("BuyModel", modelBuyList);
        }

        private void SearchSellModel(IContext ctx, ISecurity compressSource, int indexCompressBar, int indexBeginDayBar, int actualBar, List<double> sellSignal)
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
                if (pointB.Index == realPointA.Index) continue;

                // Проверм размер фигуры A-B
                var ab = realPointA.Value - pointB.Value;
                if (ab <= LengthSegmentBC || ab >= LengthSegmentAB) continue;
                
                // Проверяем приближение LowPrices на отрезке АВ к уровню точки В
                if(DistanceFromCrossing != -1 
                   && compressSource.LowPrices
                       .Skip(realPointA.Index + 2)
                       .Take(pointB.Index - realPointA.Index)
                       .Min() >= pointB.Value + DistanceFromCrossing) continue;

                var pointC = compressSource.HighPrices.
                    Select((value, index) => new { Value = value, Index = index }).
                    Skip(pointB.Index).
                    Take(indexCompressBar - pointB.Index + 1).
                    MaxBy(item => item.Value);

                // Точки B и C не могут быть на одном баре
                if (pointB.Index == pointC.Index) continue;

                // Проверям размер модели B-C
                if (pointC.Value - pointB.Value <= LengthSegmentBC ||
                    realPointA.Value - pointC.Value < 0) continue;

                // Проверяем приближение LowPrices на отрезке АС к уровню точки В
                if(DistanceFromCrossing != -1 
                   && compressSource.LowPrices
                       .Skip(pointB.Index + 2)
                       .Take(pointC.Index - pointB.Index)
                       .Min() >= pointB.Value + DistanceFromCrossing) continue;

                // Проверяем, не отработала ли уже модель
                if (indexCompressBar != pointC.Index)
                {
                    var validateMin = compressSource.LowPrices.
                        Skip(pointC.Index + 1).
                        Take(indexCompressBar - pointC.Index).
                        Min();
                    if (pointB.Value + ScopeDelta >= validateMin) continue;
                }

                // Проверка на время модели
                if (DeltaModelTimeSpan.TotalMinutes > 0 &&
                    compressSource.Bars[indexCompressBar].Date - compressSource.Bars[pointB.Index].Date > DeltaModelTimeSpan)
                    continue;

                modelSellList.Add(new TradingModel
                {
                    Value = pointB.Value
                });

                sellSignal[actualBar] = 1;
            }

            ctx.StoreObject("SellModel", modelSellList);
        }

        private List<TradingModel> ValidateBuyModel(ISecurity source, List<TradingModel> modelBuyList, int actualBar)
        {
            var lastMax = double.MinValue;

            for (var i = actualBar; i >= 0 && IsClosedBar(source.Bars[i]); i--)
            {
                lastMax = source.HighPrices[i] > lastMax ? source.HighPrices[i] : lastMax;
            }

            return modelBuyList.Where(model => model.Value - ScopeDelta > lastMax).ToList();
        }

        private List<TradingModel> ValidateSellModel(ISecurity source, List<TradingModel> modelSellList, int actualBar)
        {
            var lastMin = double.MaxValue;

            for (var i = actualBar; i >= 0 && IsClosedBar(source.Bars[i]); i--)
            {
                lastMin = source.LowPrices[i] < lastMin ? source.LowPrices[i] : lastMin;
            }

            return modelSellList.Where(model => model.Value + ScopeDelta < lastMin).ToList();
        }

        private void SearchActivePosition(ISecurity source, int actualBar)
        {
            var positionList = source.Positions.GetActiveForBar(actualBar);

            foreach (var position in positionList)
            {
                if (DeltaPositionTimeSpan.TotalMinutes > 0 &&
                    source.Bars[actualBar].Date - position.EntryBar.Date >= DeltaPositionTimeSpan)
                {
                    position.CloseAtMarket(actualBar + 1, "closeAtTime");
                    continue;
                }
                
                var arr = position.EntrySignalName.Split('_');
                switch (arr[0])
                {
                    case "buy":
                        position.CloseAtProfit(actualBar + 1, Convert.ToDouble(arr[1]) + ScopeProfit, "closeProfit");
                        position.CloseAtStop(actualBar + 1, Convert.ToDouble(arr[1]) - ScopeStop, Slippage, "closeStop");
                        break;
                    case "sell":
                        position.CloseAtProfit(actualBar + 1, Convert.ToDouble(arr[1]) - ScopeProfit, "closeProfit");
                        position.CloseAtStop(actualBar + 1, Convert.ToDouble(arr[1]) + ScopeStop, Slippage, "closeStop");
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
        
        private static bool IsClosedBar(Bar bar)
        {
            // можно гарантировать что "TotalSeconds" будет не дробным, так как система работает на интервале 5 секунд.
            return ((int)bar.Date.TimeOfDay.TotalSeconds + 5) % 60 == 0;
        }

        private bool GetValidTimeFrame(IContext ctx, ISecurity source)
        {
            if (source.IntervalBase == DataIntervals.SECONDS && source.Interval == 5) return true;
            ctx.Log("Выбран не верный таймфрейм, выберите таймфрейм равный 5 секундам", new Color(255, 0, 0), true);
            return false;
        }
        
        private int GetIndexBeginDayBar(ISecurity compressSource, DateTime dateActualBar)
        {
            return compressSource.Bars
                .Select((bar, index) => new { Index = index, Bar = bar })
                .Last(item =>
                    item.Bar.Date.TimeOfDay == TimeBeginDayBar &&
                    item.Bar.Date.Day == dateActualBar.Day &&
                    item.Bar.Date.Month == dateActualBar.Month &&
                    item.Bar.Date.Year == dateActualBar.Year).Index;
        }

        private int GetIndexCompressBar(ISecurity compressSource, DateTime dateActualBar, int indexBeginDayBar)
        {
            var indexCompressBar = indexBeginDayBar;
            var tempTime = dateActualBar - OneMinutes - FiveSeconds;
            while (compressSource.Bars[indexCompressBar].Date < tempTime)
            {
                indexCompressBar++;
            }

            return indexCompressBar;
        }
    }

    public struct TradingModel
    {
        public double Value { get; set; }
    }
}
