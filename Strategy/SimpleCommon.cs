﻿/*
 * Торговая стратегия "Strategy". Стратегия расчитана на пробитие уровней экстремума.
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
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using TSLab.DataSource;
using TSLab.Script;
using TSLab.Script.Handlers;
using TSLab.Script.Optimization;

namespace Simple
{
    public class SimpleCommon
    {
        public static List<long> modelTime = new List<long>();
        
        public OptimProperty Value = new OptimProperty(1, 0, 1000, 1);
        public OptimProperty Slippage = new OptimProperty(30, 0, 1000, 0.01);
        public OptimProperty ScopeDelta = new OptimProperty(50, 0, 1000, 0.01);
        public OptimProperty ScopeProfit = new OptimProperty(100, 0, 10000, 0.01);
        public OptimProperty ScopeStop = new OptimProperty(300, 0, 10000, 0.01);
        
        public OptimProperty LengthSegmentAB = new OptimProperty(1000, 0, 10000, 0.01);
        public OptimProperty LengthSegmentBC = new OptimProperty(390, 0, 10000, 0.01);
        public OptimProperty DistanceFromCrossing = new OptimProperty(-1, -1, 10000, 0.01);
        
        public OptimProperty DeltaModelSpan = new OptimProperty(0, 0, 1140, 1);
        public OptimProperty DeltaPositionSpan = new OptimProperty(0, 0, 1140, 1);

        public TimeSpan TimeCloseAllPosition = new TimeSpan(18, 40, 00);
        public TimeSpan TimeBeginDayBar = new TimeSpan(10, 00, 00);
        public TimeSpan FiveSeconds = new TimeSpan(0, 0, 5);
        public TimeSpan DeltaModelTimeSpan;
        public TimeSpan DeltaPositionTimeSpan;

        public int DataInterval = 5;
        public TimeSpan TimeBeginBar = new TimeSpan(10, 04, 55);
        public TimeSpan TimeOneBar = new TimeSpan(0, 5, 0);

        public void Init()
        {
            DeltaModelTimeSpan = new TimeSpan(0, DeltaModelSpan, 0);
            DeltaPositionTimeSpan = new TimeSpan(0, DeltaPositionSpan, 0);
        }
        
        public void BaseExecute(IContext ctx, ISecurity source)
        {   
            var sWatch = new Stopwatch();  
            var allWatch = new Stopwatch();  
            
            allWatch.Start();
            
            Init();
            
            // Проверяем таймфрейм входных данных
            if (!GetValidTimeFrame(ctx, source)) return;

            // Компрессия исходного таймфрейма в пятиминутный
            var compressSource = source.CompressTo(new Interval(DataInterval, DataIntervals.MINUTE), 0, 200, 0);

            // Генерация графика исходного таймфрейма
            var pain = ctx.CreatePane("Original", 70, false);
            pain.AddList(source.Symbol, compressSource, CandleStyles.BAR_CANDLE, new Color(100, 100, 100), PaneSides.RIGHT);
            pain.AddList(source.Symbol, source, CandleStyles.BAR_CANDLE, new Color(0, 0, 0), PaneSides.RIGHT);

            var indicators = AddIndicatorOnMainPain(ctx, source, pain);

            // Инцализировать индикатор моделей пустыми значениями
            var buySignal = Enumerable.Repeat((double)0, source.Bars.Count).ToList();
            var sellSignal = Enumerable.Repeat((double)0, source.Bars.Count).ToList();

            for (var historyBar = 1; historyBar <= source.Bars.Count - 1; historyBar++)
            {
                Trading(ctx, source, compressSource, historyBar, buySignal, sellSignal, indicators, sWatch);
            }
            
            var buyPain = ctx.CreatePane("BuySignal", 15, false);
            buyPain.AddList("BuySignal", buySignal, ListStyles.HISTOHRAM_FILL, new Color(0, 255, 0), LineStyles.SOLID,
                PaneSides.RIGHT);

            var sellPain = ctx.CreatePane("SellSignal", 15, false);
            sellPain.AddList("SellSignal", sellSignal, ListStyles.HISTOHRAM_FILL, new Color(255, 0, 0), LineStyles.SOLID,
                PaneSides.RIGHT);
            
            allWatch.Stop();
            
            ctx.Log("Время общее: " + allWatch.ElapsedTicks);
            
            ctx.Log("modelTime: " + modelTime.Sum());
        }

        protected virtual Indicators AddIndicatorOnMainPain(IContext ctx, ISecurity source, IPane pain)
        {
            // Добавлять индикаторы не требуется
            return new Indicators();
        }

        private void Trading(IContext ctx,
            ISecurity source,
            ISecurity compressSource,
            int actualBar,
            List<double> buySignal,
            List<double> sellSignal,
            Indicators indicators, Stopwatch sWatch)
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
                SearchActivePosition(source, actualBar, indicators);
            }
                        
            sWatch.Start();
            
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
            
            sWatch.Stop();
            modelTime.Add(sWatch.ElapsedTicks);
            sWatch.Reset();
            
            var modelBuyList = (List<TradingModel>)ctx.LoadObject("BuyModel") ?? new List<TradingModel>();
            if (modelBuyList.Any())
            {
                var buyList = ValidateBuyModel(source, modelBuyList, actualBar);
                
                foreach (var model in buyList)
                {
                    CreateBuyOrder(source, actualBar, model, indicators);
                }
                
                ctx.StoreObject("BuyModel", buyList);
            }
            
            var modelSellList = (List<TradingModel>)ctx.LoadObject("SellModel") ?? new List<TradingModel>();
            if (modelSellList.Any())
            {
                var sellList = ValidateSellModel(source, modelSellList, actualBar);
                foreach (var model in sellList)
                {
                    CreateSellOrder(source, actualBar, model, indicators);
                }
                ctx.StoreObject("SellList", sellList);
            }
        }

        public virtual void CreateBuyOrder(ISecurity source, int actualBar, TradingModel model, Indicators indicators)
        {
            source.Positions.BuyIfGreater(actualBar + 1, Value, model.EnterPrice, Slippage, "buy_" + model.GetNamePosition);
        }

        public virtual void CreateSellOrder(ISecurity source, int actualBar, TradingModel model, Indicators indicators)
        {
            source.Positions.SellIfLess(actualBar + 1, Value, model.EnterPrice, Slippage, "sell_" + model.GetNamePosition);
        }

        private void SearchBuyModel(IContext ctx, ISecurity compressSource, int indexCompressBar, int indexBeginDayBar, int actualBar, List<double> buySignal)
        {
            var modelBuyList = new List<TradingModel>();

            for (var indexPointA = indexCompressBar - 1; indexPointA >= indexBeginDayBar && indexPointA >= 0; indexPointA--)
            {
                var pointB = MaxByValue(compressSource.Bars
                    .Select((bar, index) => new PointModel{ Value = bar.High, Index = index })
                    .Skip(indexPointA)
                    .Take(indexCompressBar - indexPointA + 1)
                    .ToList());

                var realPointA = MinByValue(compressSource.Bars
                    .Select((bar, index) => new PointModel{ Value = bar.Low, Index = index })
                    .Skip(indexPointA)
                    .Take(pointB.Index - indexPointA + 1)
                    .ToList());

                // Точки A и B не могут быть на одном баре
                if (pointB.Index == realPointA.Index) continue;

                // Проверм размер фигуры A-B
                var ab = pointB.Value - realPointA.Value;
                if (ab <= LengthSegmentBC || ab >= LengthSegmentAB) continue;
                
                // Проверяем приближение HighPrices на отрезке АВ к уровню точки В
                if(DistanceFromCrossing != -1
                   && pointB.Index - realPointA.Index - 1 > 0
                   && compressSource.Bars
                       .Skip(realPointA.Index + 1)
                       .Take(pointB.Index - realPointA.Index - 1)
                       .Select(x => x.High)
                       .Max() >= pointB.Value - DistanceFromCrossing) continue;

                var pointC = MinByValue(compressSource.Bars
                    .Select((bar, index) => new PointModel{ Value = bar.Low, Index = index })
                    .Skip(pointB.Index)
                    .Take(indexCompressBar - pointB.Index + 1)
                    .ToList());

                // Точки B и C не могут быть на одном баре
                if (pointB.Index == pointC.Index) continue;

                // Проверям размер модели B-C
                var bc = pointB.Value - pointC.Value;
                if (bc <= LengthSegmentBC || pointC.Value - realPointA.Value < 0) continue;
                
                // Проверяем приближение HighPrices на отрезке АС к уровню точки В
                if(DistanceFromCrossing != -1 
                   && pointC.Index - pointB.Index - 1 > 0
                   && compressSource.Bars
                       .Skip(pointB.Index + 1)
                       .Take(pointC.Index - pointB.Index - 1)
                       .Select(x => x.High)
                       .Max() >= pointB.Value - DistanceFromCrossing) continue;
                
                var model = GetNewBuyTradingModel(pointB.Value, bc);
                // Проверяем, не отработала ли уже модель
                if (indexCompressBar != pointC.Index)
                { 
                    var validateMax = compressSource.Bars
                        .Skip(pointC.Index + 1)
                        .Take(indexCompressBar - pointC.Index)
                        .Select(x => x.High)
                        .Max();
                    if (model.EnterPrice <= validateMax) continue;
                }

                // Проверка на время модели
                if (DeltaModelTimeSpan.TotalMinutes > 0 &&
                    compressSource.Bars[indexCompressBar].Date - compressSource.Bars[pointB.Index].Date > DeltaModelTimeSpan)
                    continue;

                modelBuyList.Add(model);
                buySignal[actualBar] = 1;
            }

            ctx.StoreObject("BuyModel", modelBuyList);
        }

        private void SearchSellModel(IContext ctx, ISecurity compressSource, int indexCompressBar, int indexBeginDayBar, int actualBar, List<double> sellSignal)
        {
            List<TradingModel> modelSellList = new List<TradingModel>();

            for (var indexPointA = indexCompressBar - 1; indexPointA >= indexBeginDayBar && indexPointA >= 0; indexPointA--)
            {
                var pointB = MinByValue(compressSource.Bars
                    .Select((bar, index) => new PointModel{ Value = bar.Low, Index = index })
                    .Skip(indexPointA)
                    .Take(indexCompressBar - indexPointA + 1)
                    .ToList());

                var realPointA = MaxByValue(compressSource.Bars
                    .Select((bar, index) => new PointModel{ Value = bar.High, Index = index })
                    .Skip(indexPointA)
                    .Take(pointB.Index - indexPointA + 1)
                    .ToList());

                // Точки A и B не могут быть на одном баре
                if (pointB.Index == realPointA.Index) continue;

                // Проверм размер фигуры A-B
                var ab = realPointA.Value - pointB.Value;
                if (ab <= LengthSegmentBC || ab >= LengthSegmentAB) continue;
                
                // Проверяем приближение LowPrices на отрезке АВ к уровню точки В
                if(DistanceFromCrossing != -1 
                   && pointB.Index - realPointA.Index - 1 > 0
                   && compressSource.Bars
                       .Skip(realPointA.Index + 1)
                       .Take(pointB.Index - realPointA.Index - 1)
                       .Select(x => x.Low)
                       .Min() <= pointB.Value + DistanceFromCrossing) continue;

                var pointC = MaxByValue(compressSource.Bars
                    .Select((bar, index) => new PointModel{ Value = bar.High, Index = index })
                    .Skip(pointB.Index)
                    .Take(indexCompressBar - pointB.Index + 1)
                    .ToList());

                // Точки B и C не могут быть на одном баре
                if (pointB.Index == pointC.Index) continue;

                // Проверям размер модели B-C
                var bc = pointC.Value - pointB.Value;
                if (bc <= LengthSegmentBC || realPointA.Value - pointC.Value < 0) continue;

                // Проверяем приближение LowPrices на отрезке АС к уровню точки В
                if(DistanceFromCrossing != -1 
                   && pointC.Index - pointB.Index - 1 > 0
                   && compressSource.Bars
                       .Skip(pointB.Index + 1)
                       .Take(pointC.Index - pointB.Index - 1)
                       .Select(x => x.Low)
                       .Min() <= pointB.Value + DistanceFromCrossing) continue;
                
                var model = GetNewSellTradingModel(pointB.Value, bc);
                // Проверяем, не отработала ли уже модель
                if (indexCompressBar != pointC.Index)
                {
                    var validateMin = compressSource.Bars
                        .Skip(pointC.Index + 1)
                        .Take(indexCompressBar - pointC.Index)
                        .Select(x => x.Low)
                        .Min();
                    if (model.EnterPrice >= validateMin) continue;
                }

                // Проверка на время модели
                if (DeltaModelTimeSpan.TotalMinutes > 0 &&
                    compressSource.Bars[indexCompressBar].Date - compressSource.Bars[pointB.Index].Date > DeltaModelTimeSpan)
                    continue;

                modelSellList.Add(model);
                sellSignal[actualBar] = 1;
            }

            ctx.StoreObject("SellModel", modelSellList);
        }

        private List<TradingModel> ValidateBuyModel(ISecurity source, List<TradingModel> modelBuyList, int actualBar)
        {
            var lastMax = double.MinValue;

            for (var i = actualBar; i >= 0 && !IsClosedBar(source.Bars[i]); i--)
            {
                lastMax = source.Bars[i].High > lastMax ? source.Bars[i].High : lastMax;
            }

            return modelBuyList.Where(model => model.EnterPrice > lastMax).ToList();
        }

        private List<TradingModel> ValidateSellModel(ISecurity source, List<TradingModel> modelSellList, int actualBar)
        {
            var lastMin = double.MaxValue;

            for (var i = actualBar; i >= 0 && !IsClosedBar(source.Bars[i]); i--)
            {
                lastMin = source.Bars[i].Low < lastMin ? source.Bars[i].Low : lastMin;
            }

            return modelSellList.Where(model => model.EnterPrice < lastMin).ToList();
        }

        private void SearchActivePosition(ISecurity source, int actualBar, Indicators indicators)
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
                        SetBuyProfit(actualBar, position, arr);
                        SetBuyStop(actualBar, position, arr, indicators);
                        break;
                    case "sell":
                        SetSellProfit(actualBar, position, arr);
                        SetSellStop(actualBar, position, arr, indicators);
                        break;
                }
            }
        }

        protected virtual void SetBuyProfit(int actualBar, IPosition position, string[] arr)
        {
            position.CloseAtProfit(actualBar + 1, Convert.ToDouble(arr[4]), "closeProfit");
        }
        
        protected virtual void SetBuyStop(int actualBar, IPosition position, string[] arr, Indicators indicators)
        {
            position.CloseAtStop(actualBar + 1, Convert.ToDouble(arr[3]), Convert.ToDouble(Slippage), "closeStop");
        }
        
        protected virtual void SetSellProfit(int actualBar, IPosition position, string[] arr)
        {
            position.CloseAtProfit(actualBar + 1, Convert.ToDouble(arr[4]), "closeProfit");
        }

        protected virtual void SetSellStop(int actualBar, IPosition position, string[] arr, Indicators indicators)
        {
            position.CloseAtStop(actualBar + 1, Convert.ToDouble(arr[3]), Convert.ToDouble(Slippage), "closeStop");
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

        private bool GetValidTimeFrame(IContext ctx, ISecurity source)
        {
            if (source.IntervalBase == DataIntervals.SECONDS && source.Interval == 5) return true;
            ctx.Log("Выбран не верный таймфрейм, выберите таймфрейм равный 5 секундам");
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
            var tempTime = dateActualBar - TimeOneBar - FiveSeconds;
            while (compressSource.Bars[indexCompressBar].Date < tempTime)
            {
                indexCompressBar++;
            }

            return indexCompressBar;
        }
        
        private bool IsClosedBar(IDataBar bar)
        {
            // можно гарантировать что "TotalSeconds" будет не дробным, так как система работает на интервале 5 секунд.
            return ((int)bar.Date.TimeOfDay.TotalSeconds + 5) % (DataInterval * 60) == 0;
        }
        
        protected virtual TradingModel GetNewBuyTradingModel(double value, double bc)
        {
            return new TradingModel
            {
                Value = value,
                EnterPrice = value - ScopeDelta,
                StopPrice = value - ScopeStop,
                ProfitPrice = value + ScopeProfit
            };
        }

        protected virtual TradingModel GetNewSellTradingModel(double value, double bc)
        {
            return new TradingModel
            {
                Value = value,
                EnterPrice = value + ScopeDelta,
                StopPrice = value + ScopeStop,
                ProfitPrice = value - ScopeProfit
            };
        }
        
        public static PointModel MinByValue(IReadOnlyCollection<PointModel> source)
        {
            var min = source.First();
            
            foreach (var model in source)
            {
                if (model.Value < min.Value)
                {
                    min = model;
                }
            }

            return min;
        }
        
        public static PointModel MaxByValue(IReadOnlyCollection<PointModel> source)
        {
            var max = source.First();
            
            foreach (var model in source)
            {
                if (model.Value > max.Value)
                {
                    max = model;
                }
            }

            return max;
        }
    }
    
    public class TradingModel
    {
        public double Value { get; set; }
        
        public double EnterPrice { get; set; }

        public double StopPrice { get; set; }

        public double ProfitPrice { get; set; }

        public string GetNamePosition
        {
            get { return Value + "_" + EnterPrice + "_" + StopPrice + "_" + ProfitPrice; }
        }
    }
    
    public class PointModel
    {
        public double Value { get; set; }
        
        public int Index { get; set; }
    }
    
    public class Indicators
    {
        public IList<double> Parabolic { get; set; }
        
        public IList<double> EMA { get; set; }
    }
}