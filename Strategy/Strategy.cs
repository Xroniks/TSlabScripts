/*
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
 *  - DeltaModelSpan, ограничивает время ожидания ВХОДА в позицию. При значении "-1" ограничение не работает. Ограничение задается в минутах.
 *  - DeltaPositionSpan, ограничивает время ожидания ВЫХОДА из позиции. При значении "-1" ограничение не работает. Ограничение задается в минутах.
 *
 *  - MultyPosition, при 0 - не выставляет ордера на вход если есть активная позиция. 1 - включено, 0 - выключено
 *  - DataInterval, таймфрейм для формирования моделей. 1 или 5 минут (с иными вариантами не тестировалось)
 * 
 *  - StartTime, время с которого НАЧНУТ выставляться ордера на вход. 10:00:00 => 100000
 *  - StopTime, время с которого ЗАКОНЧАТ выставляться ордера на вход. 18:00:00 => 180000
 * 
*/

using System;
using System.Collections.Generic;
using System.Linq;
using TSLab.DataSource;
using TSLab.Script;
using TSLab.Script.Handlers;
using TSLab.Script.Optimization;

namespace Simple
{
    public class Strategy : IExternalScript
    {
        // запретить public OptimProperty MultyPosition = new OptimProperty(0, 0, 1, 1);
        public OptimProperty DataInterval = new OptimProperty(5, 1, 5, 1);
        // public OptimProperty ReverseMode = new OptimProperty(0, 0, 1, 1);
        
        public OptimProperty StartTime = new OptimProperty(100000, 100000, 240000, 1);
        public OptimProperty StopTime = new OptimProperty(180000, 100000, 240000, 1);
        
        public OptimProperty Value = new OptimProperty(1, 0, 1000, 1);
        public OptimProperty Slippage = new OptimProperty(30, 0, 1000, 0.01);
        public OptimProperty ScopeDelta = new OptimProperty(50, 0, 1000, 0.01);
        public OptimProperty ScopeProfit = new OptimProperty(100, 0, 10000, 0.01);
        public OptimProperty ScopeStop = new OptimProperty(300, 0, 10000, 0.01);
        
        public OptimProperty LengthSegmentAB = new OptimProperty(1000, 0, 10000, 0.01);
        public OptimProperty LengthSegmentBC = new OptimProperty(390, 0, 10000, 0.01);
        //public OptimProperty DistanceFromCrossing = new OptimProperty(-1, -1, 10000, 0.01);
        
        //public OptimProperty DeltaModelSpan = new OptimProperty(-1, -1, 1140, 1);
        //public OptimProperty DeltaPositionSpan = new OptimProperty(-1, -1, 1140, 1);
        
        public OptimProperty EnableParabolic = new OptimProperty(0, 0, 1, 1);
        public OptimProperty AccelerationMax = new OptimProperty(0.02, 0.01, 1, 0.01);
        public OptimProperty AccelerationStart = new OptimProperty(0.02, 0.01, 1, 0.01);
        public OptimProperty AccelerationStep = new OptimProperty(0.02, 0.01, 1, 0.01);
        
        public OptimProperty EnableCoefficient = new OptimProperty(0, 0, 1, 1);
        public OptimProperty MultyplayDelta = new OptimProperty(1.03, 1.0, 2.0, double.MaxValue);
        public OptimProperty MultyplayProfit = new OptimProperty(1011.0 / 1000.0, 1.0, 2.0, double.MaxValue);
        public OptimProperty MultyplayStop = new OptimProperty(1.0065, 1.0, 2.0, double.MaxValue);
        public OptimProperty MultyplayDivider = new OptimProperty(10, 1.0, 1000, 10);
        public OptimProperty PriceStep = new OptimProperty(10, 0.001, 100, double.MaxValue);
        
        //public OptimProperty EnableEMA = new OptimProperty(0, 0, 1, 1);
        //public OptimProperty PeriodEMA = new OptimProperty(34, 1, 1000, 0.01);
        
        public TimeSpan FiveSeconds = new TimeSpan(0, 0, 5);
        public TimeSpan StartTimeTimeSpan;
        public TimeSpan StopTimeTimeSpan;
        public Boolean IsReverseMode;
        public Boolean IsCoefficient;
        public Boolean IsEMA;
        public Boolean IsParabolic;

        public IContext Logger; 

        public string ShortModelKey = "ShortModel";
        public string LongModelKey = "LongModel";

        public void Init()
        {
            var StartTimeString = StartTime.Value.ToString();
            StartTimeTimeSpan = new TimeSpan(
                Convert.ToInt32(StartTimeString.Substring(0, 2)), 
                Convert.ToInt32(StartTimeString.Substring(2, 2)), 
                Convert.ToInt32(StartTimeString.Substring(4, 2)));

            var StopTimeString = StopTime.Value.ToString();
            StopTimeTimeSpan = new TimeSpan(
                Convert.ToInt32(StopTimeString.Substring(0, 2)), 
                Convert.ToInt32(StopTimeString.Substring(2, 2)), 
                Convert.ToInt32(StopTimeString.Substring(4, 2)));

            IsReverseMode = ReverseMode > 0;
            IsCoefficient = EnableCoefficient > 0;
            IsEMA = EnableEMA > 0;
            IsParabolic = EnableParabolic > 0;
        }
        
        public void Execute(IContext ctx, ISecurity source)
        {
            Logger = ctx;
            
            Init();
            
            // Проверяем таймфрейм входных данных
            if (!GetValidTimeFrame(ctx, source)) return;

            // Компрессия исходного таймфрейма в пятиминутный
            var compressSource = source.CompressTo(new Interval(DataInterval, DataIntervals.MINUTE));
            var hourlySource = source.CompressTo(new Interval(60, DataIntervals.MINUTE));

            // Генерация графика исходного таймфрейма
            var pain = ctx.CreatePane("Original", 90, false);

            pain.AddList(source.Symbol, compressSource, CandleStyles.BAR_CANDLE, new Color(100, 100, 100), PaneSides.RIGHT);
            pain.AddList(source.Symbol, source, CandleStyles.BAR_CANDLE, new Color(0, 0, 0), PaneSides.RIGHT);

            var indicators = AddIndicatorOnMainPain(ctx, source, pain);

            // Инцализировать индикатор моделей пустыми значениями
            var barsCount = source.Bars.Count;
            var buySignal = Enumerable.Repeat((double)0, barsCount).ToList();
            var sellSignal = Enumerable.Repeat((double)0, barsCount).ToList();

            for (var historyBar = 1; historyBar < source.Bars.Count; historyBar++)
            {
                Trading(ctx, source, compressSource, hourlySource, historyBar, buySignal, sellSignal, indicators);
            }
            
            var buyPain = ctx.CreatePane("BuySignal", 5, false);
            buyPain.AddList("BuySignal", buySignal, ListStyles.HISTOHRAM_FILL, new Color(0, 255, 0), LineStyles.SOLID,
                PaneSides.RIGHT);

            var sellPain = ctx.CreatePane("SellSignal", 5, false);
            sellPain.AddList("SellSignal", sellSignal, ListStyles.HISTOHRAM_FILL, new Color(255, 0, 0), LineStyles.SOLID,
                PaneSides.RIGHT);
        }

        protected virtual Indicators AddIndicatorOnMainPain(IContext ctx, ISecurity source, IPane pain)
        {
            var indicators = new Indicators();

            if (EnableParabolic == 1)
            {
                var parabolic = ctx.GetData("Parabolic", new[] {""}, () => new ParabolicSAR
                {
                    AccelerationMax = AccelerationMax,
                    AccelerationStart = AccelerationStart,
                    AccelerationStep = AccelerationStep
                }.Execute(source));
                var nameParabolic = "Parabolic (" + AccelerationStart.Value + "," + AccelerationStep.Value + "," +
                                    AccelerationMax.Value + ")";
                pain.AddList(nameParabolic, parabolic, ListStyles.LINE, new Color(255, 0, 0), LineStyles.SOLID,
                    PaneSides.RIGHT);

                indicators.Parabolic = parabolic;
            }

            if (EnableEMA == 1)
            {
                var ema = ctx.GetData("EMA", new[] {""}, () => new EMA
                {
                    Period = PeriodEMA
                }.Execute(source.ClosePrices));
                var nameEma = "EMA (" + PeriodEMA + ")";
                pain.AddList(nameEma, ema, ListStyles.LINE, new Color(0, 255, 0), LineStyles.SOLID, PaneSides.RIGHT);

                indicators.EMA = ema;
            }

            return indicators;
        }

        private void Trading(IContext ctx,
            ISecurity source,
            ISecurity compressSource,
            ISecurity hourlySource,
            int actualBar,
            IList<double> buySignal,
            IList<double> sellSignal,
            Indicators indicators)
        {
            if (source.Bars[actualBar].Date.TimeOfDay < GetTimeBeginBar()) return;
            
            // Если время 18:40 или более - закрыть все активные позиции и не торговать
            if (source.Bars[actualBar].Date.TimeOfDay >= StopTimeTimeSpan)
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

            if (IsClosedBar(source.Bars[actualBar]))
            {
                var dateActualBar = source.Bars[actualBar].Date;
                var indexBeginDayBar = GetIndexBeginDayBar(compressSource, dateActualBar);
                var indexCompressBar = GetIndexCompressBar(compressSource, dateActualBar, indexBeginDayBar);

                // Поиск моделей, информация о моделях пишется в "StoreObject"
                // Модели ищутся только на открытии бара, а валидируются каждые 5 секунд

                var highPoints = compressSource.Bars
                    .Skip(indexBeginDayBar)
                    .Select((bar, index) => new PointModel {Value = bar.High, Index = index + indexBeginDayBar})
                    .ToList();
                
                var lowPoints = compressSource.Bars
                    .Skip(indexBeginDayBar)
                    .Select((bar, index) => new PointModel {Value = bar.Low, Index = index + indexBeginDayBar})
                    .ToList();
                
                SearchBuyModel(ctx, compressSource, indexCompressBar, indexBeginDayBar, actualBar, buySignal, sellSignal, highPoints, lowPoints);
                SearchSellModel(ctx, compressSource, indexCompressBar, indexBeginDayBar, actualBar, sellSignal, buySignal, highPoints, lowPoints);
            }

            try
            {
                CreateOpenPositionOrder(ctx, source, actualBar, indicators);
            }
            catch (Exception e)
            {
                ctx.Log(e.ToString());
            }
        }

        private void CreateOpenPositionOrder(IContext ctx, ISecurity source, int actualBar, Indicators indicators)
        {
            var timeActualBar = source.Bars[actualBar].Date.TimeOfDay;
            var canOpenPosition = (MultyPosition > 0 || source.Positions.ActivePositionCount == 0)
                                  && timeActualBar > StartTimeTimeSpan && timeActualBar < StopTimeTimeSpan;

            var modelBuyList = (ctx.LoadObject(LongModelKey) ?? new List<TradingModel>()) as List<TradingModel>;
            var modelSellList = (ctx.LoadObject(ShortModelKey) ?? new List<TradingModel>()) as List<TradingModel>;

            if (modelBuyList != null && modelBuyList.Any())
            {
                var buyList = ValidateBuyModel(ctx, source, modelBuyList, actualBar);

                if (canOpenPosition)
                {
                    foreach (var model in buyList)
                    {
                        if (source.Bars[actualBar].Date.TimeOfDay >= new TimeSpan(12, 8, 55) && source.Bars[actualBar].Date.TimeOfDay <= new TimeSpan(12, 9, 0))
                        {
                            Logger.Log("date: " + source.Bars[actualBar].Date + " " + model.Value);
                        }
                        
                        CreateLongOrder(source, actualBar, model, indicators);
                    }
                }

                ctx.StoreObject(LongModelKey, buyList);
            }

            if (modelSellList != null && modelSellList.Any())
            {
                var sellList = ValidateSellModel(ctx, source, modelSellList, actualBar);

                if (canOpenPosition)
                {
                    foreach (var model in sellList)
                    {
                        CreateShortOrder(source, actualBar, model, indicators);
                    }
                }

                ctx.StoreObject(ShortModelKey, sellList);
            }
        }

        public virtual void CreateLongOrder(ISecurity source, int actualBar, TradingModel model, Indicators indicators)
        {
            if (IsEMA)
            {
                var emaValue = indicators.EMA[actualBar];
                if (emaValue > model.EnterPrice)
                {
                    return;
                }
            }
            
            if (IsParabolic)
            {
                var parabolicValue = indicators.Parabolic[actualBar];
                if (parabolicValue > model.EnterPrice)
                {
                    return;
                }
            }
            
            if (IsReverseMode)
            {
                source.Positions.SellAtPrice(
                    actualBar + 1,
                    Value, model.EnterPrice,
                    "sell_" + model.GetNamePosition + "_reverse");
                return;
            }
            
            source.Positions.BuyIfGreater(
                    actualBar + 1,
                    Value, model.EnterPrice,
                    Slippage,
                    "buy_" + model.GetNamePosition);
        }

        public virtual void CreateShortOrder(ISecurity source, int actualBar, TradingModel model, Indicators indicators)
        {
            if (IsEMA)
            {
                var emaValue = indicators.EMA[actualBar];
                if (emaValue < model.EnterPrice)
                {
                    return;
                }
            }
            
            if (IsParabolic)
            {
                var parabolicValue = indicators.Parabolic[actualBar];
                if (parabolicValue < model.EnterPrice)
                {
                    return;
                }
            }

            if (IsReverseMode)
            {
                source.Positions.BuyAtPrice(
                    actualBar + 1,
                    Value, model.EnterPrice,
                    "buy_" + model.GetNamePosition + "_reverse");
                return;
            }
            
            source.Positions.SellIfLess(
                    actualBar + 1, 
                    Value, model.EnterPrice, 
                    Slippage, 
                    "sell_" + model.GetNamePosition);
        }

        private void SearchBuyModel(
            IContext ctx, 
            ISecurity compressSource, 
            int indexCompressBar, 
            int indexBeginDayBar,
            int actualBar, 
            IList<double> buySignal, 
            IList<double> sellSignal, 
            IReadOnlyCollection<PointModel> highPoints, 
            IReadOnlyCollection<PointModel> lowPoints)
        {
            var models = new List<TradingModel>();

            for (var indexPointA = indexCompressBar - 1; indexPointA >= indexBeginDayBar && indexPointA >= 0; indexPointA--)
            {
                var pointB = MaxByValue(highPoints
                    .Skip(indexPointA - indexBeginDayBar)
                    .Take(indexCompressBar - indexPointA + 1)
                    .ToArray());

                var realPointA = MinByValue(lowPoints
                    .Skip(indexPointA - indexBeginDayBar)
                    .Take(pointB.Index - indexPointA + 1)
                    .ToArray());
                
                // Точки A и B не могут быть на одном баре
                if (pointB.Index == realPointA.Index) continue;

                // Проверм размер фигуры A-B
                var ab = pointB.Value - realPointA.Value;
                if (ab <= LengthSegmentBC || ab >= LengthSegmentAB) continue;
                
                // Проверяем приближение HighPrices на отрезке АВ к уровню точки В
                if(DistanceFromCrossing != -1
                   && pointB.Index - realPointA.Index - 1 > 0
                   && MaxByValue(highPoints
                       .Skip(realPointA.Index + 1 - indexBeginDayBar)
                       .Take(pointB.Index - realPointA.Index - 1)
                       .ToArray()).Value >= pointB.Value - DistanceFromCrossing) continue;
                
                var pointC = MinByValue(lowPoints
                    .Skip(pointB.Index - indexBeginDayBar)
                    .Take(indexCompressBar - pointB.Index + 1)
                    .ToArray());
                
                // Точки B и C не могут быть на одном баре
                if (pointB.Index == pointC.Index) continue;

                // Проверям размер модели B-C
                var bc = pointB.Value - pointC.Value;
                if (bc <= LengthSegmentBC || pointC.Value - realPointA.Value < 0) continue;
                
                // Проверяем приближение HighPrices на отрезке АС к уровню точки В
                if(DistanceFromCrossing != -1 
                   && pointC.Index - pointB.Index - 1 > 0
                   && highPoints
                       .Skip(pointB.Index + 1 - indexBeginDayBar)
                       .Take(pointC.Index - pointB.Index - 1)
                       .Select(x => x.Value)
                       .Max() >= pointB.Value - DistanceFromCrossing) continue;
                
                var model = GetNewLongTradingModel(pointB.Value, bc);
                
                // Проверяем, не отработала ли уже модель
                if (indexCompressBar != pointC.Index)
                { 
                    var validateMax = highPoints
                        .Skip(pointC.Index + 1 - indexBeginDayBar)
                        .Take(indexCompressBar - pointC.Index)
                        .Select(x => x.Value)
                        .Max();
                    if (model.EnterPrice <= validateMax) continue;
                }

                models.Add(model);
                buySignal[actualBar] = 1;
            }

            ctx.StoreObject(LongModelKey, models);
        }

        private void SearchSellModel(
            IContext ctx, 
            ISecurity compressSource, 
            int indexCompressBar, 
            int indexBeginDayBar, 
            int actualBar, 
            IList<double> sellSignal,
            IList<double> buySignal, 
            IReadOnlyCollection<PointModel> highPoints, 
            IReadOnlyCollection<PointModel> lowPoints)
        {
            var modelSellList = new List<TradingModel>();

            for (var indexPointA = indexCompressBar - 1; indexPointA >= indexBeginDayBar && indexPointA >= 0; indexPointA--)
            {
                var pointB = MinByValue(lowPoints
                    .Skip(indexPointA - indexBeginDayBar)
                    .Take(indexCompressBar - indexPointA + 1)
                    .ToArray());

                var realPointA = MaxByValue(highPoints
                    .Skip(indexPointA - indexBeginDayBar)
                    .Take(pointB.Index - indexPointA + 1)
                    .ToArray());

                // Точки A и B не могут быть на одном баре
                if (pointB.Index == realPointA.Index) continue;

                // Проверм размер фигуры A-B
                var ab = realPointA.Value - pointB.Value;
                if (ab <= LengthSegmentBC || ab >= LengthSegmentAB) continue;
                
                // Проверяем приближение LowPrices на отрезке АВ к уровню точки В
                if(DistanceFromCrossing != -1 
                   && pointB.Index - realPointA.Index - 1 > 0
                   && lowPoints
                       .Skip(realPointA.Index + 1 - indexBeginDayBar)
                       .Take(pointB.Index - realPointA.Index - 1)
                       .Select(x => x.Value)
                       .Min() <= pointB.Value + DistanceFromCrossing) continue;

                var pointC = MaxByValue(highPoints
                    .Skip(pointB.Index - indexBeginDayBar)
                    .Take(indexCompressBar - pointB.Index + 1)
                    .ToArray());

                // Точки B и C не могут быть на одном баре
                if (pointB.Index == pointC.Index) continue;

                // Проверям размер модели B-C
                var bc = pointC.Value - pointB.Value;
                if (bc <= LengthSegmentBC || realPointA.Value - pointC.Value < 0) continue;

                // Проверяем приближение LowPrices на отрезке АС к уровню точки В
                if(DistanceFromCrossing != -1 
                   && pointC.Index - pointB.Index - 1 > 0
                   && lowPoints
                       .Skip(pointB.Index + 1 - indexBeginDayBar)
                       .Take(pointC.Index - pointB.Index - 1)
                       .Select(x => x.Value)
                       .Min() <= pointB.Value + DistanceFromCrossing) continue;
                
                var model = GetNewShortTradingModel(pointB.Value, bc);
                
                // Проверяем, не отработала ли уже модель
                if (indexCompressBar != pointC.Index)
                {
                    var validateMin = lowPoints
                        .Skip(pointC.Index + 1 - indexBeginDayBar)
                        .Take(indexCompressBar - pointC.Index)
                        .Select(x => x.Value)
                        .Min();
                    if (model.EnterPrice >= validateMin) continue;
                }

                modelSellList.Add(model);
                sellSignal[actualBar] = 1;
            }

            ctx.StoreObject(ShortModelKey, modelSellList);
        }

        private List<TradingModel> ValidateBuyModel(
            IContext ctx, 
            ISecurity source,
            IReadOnlyCollection<TradingModel> modelBuyList,
            int actualBar)
        {
            var lastMax = double.MinValue;

            for (var i = actualBar; i >= 0 && !IsClosedBar(source.Bars[i]); i--)
            {
                lastMax = source.Bars[i].High > lastMax ? source.Bars[i].High : lastMax;
            }

            return modelBuyList.Where(model => model.EnterPrice > lastMax).ToList();
        }

        private List<TradingModel> ValidateSellModel(
            IContext ctx,
            ISecurity source, 
            IReadOnlyCollection<TradingModel> modelSellList, 
            int actualBar)
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
                var arr = position.EntrySignalName.Split('_');
                switch (arr[0])
                {
                    case "buy":
                        SetLongProfit(actualBar, position, arr, indicators);
                        SetLongStop(actualBar, position, arr, indicators);
                        break;
                    case "sell":
                        SetShortProfit(actualBar, position, arr, indicators);
                        SetShortStop(actualBar, position, arr, indicators);
                        break;
                }
            }
        }

        protected virtual void SetLongStop(int actualBar, IPosition position, string[] arr, Indicators indicators)
        {
            var prises = new List<double>{ Convert.ToDouble(arr[3]) };

            if (!IsReverseMode && IsParabolic)
            {
                prises.Add(Convert.ToDouble(indicators.Parabolic[actualBar]));
            }

            position.CloseAtStop(actualBar + 1, prises.Max(), Convert.ToDouble(Slippage), "closeStop");
        }
        
        protected virtual void SetShortStop(int actualBar, IPosition position, string[] arr, Indicators indicators)
        {
            var prises = new List<double>{ Convert.ToDouble(arr[3]) };

            if (!IsReverseMode && IsParabolic)
            {
                prises.Add(Convert.ToDouble(indicators.Parabolic[actualBar]));
            }

            position.CloseAtStop(actualBar + 1, prises.Min(), Convert.ToDouble(Slippage), "closeStop");
        }
        
        protected virtual void SetLongProfit(int actualBar, IPosition position, string[] arr, Indicators indicators)
        {
            var prises = new List<double>{ Convert.ToDouble(arr[4]) };
            
            if (IsReverseMode && IsParabolic)
            {
                prises.Add(Convert.ToDouble(indicators.Parabolic[actualBar]));
            }
            
            position.CloseAtProfit(actualBar + 1, prises.Min(), "closeProfit");
        }
        
        protected virtual void SetShortProfit(int actualBar, IPosition position, string[] arr, Indicators indicators)
        {
            var prises = new List<double>{ Convert.ToDouble(arr[4]) };
            
            if (IsReverseMode && IsParabolic)
            {
                prises.Add(Convert.ToDouble(indicators.Parabolic[actualBar]));
            }
            
            position.CloseAtProfit(actualBar + 1, prises.Max(), "closeProfit");
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
            var result = compressSource.Bars.ToList().FindLastIndex(item =>
                item.Date.TimeOfDay == StartTimeTimeSpan &&
                item.Date.Day == dateActualBar.Day &&
                item.Date.Month == dateActualBar.Month &&
                item.Date.Year == dateActualBar.Year);
            return result >= 0 ? result : 0;
          
        }

        private int GetIndexCompressBar(ISecurity compressSource, DateTime dateActualBar, int indexBeginDayBar)
        {
            var indexCompressBar = indexBeginDayBar;
            var tempTime = dateActualBar - GetTimeOneBar() - FiveSeconds;
            
            while (compressSource.Bars[indexCompressBar].Date < tempTime)
            {
                indexCompressBar++;
            }

            return indexCompressBar;
        }
        
        public bool IsClosedBar(IDataBar bar)
        {
            // можно гарантировать что "TotalSeconds" будет не дробным, так как система работает на интервале 5 секунд.
            return ((int)bar.Date.TimeOfDay.TotalSeconds + 5) % (DataInterval * 60) == 0;
        }

        protected virtual TradingModel GetNewLongTradingModel(double value, double bc)
        {
            var enterPriceDelta = IsCoefficient ? CalculatePrice(bc, MultyplayDelta) : ScopeDelta;
            var stopPriceDelta = IsCoefficient ? CalculatePrice(bc, MultyplayStop) : ScopeStop;
            var profitPriceDelta = IsCoefficient ? CalculatePrice(bc, MultyplayProfit) : ScopeProfit;

            var model = new TradingModel
            {
                Value = value,
                EnterPrice = value - enterPriceDelta
            };
            
            if (IsReverseMode)
            {
                model.StopPrice = value + stopPriceDelta;
                model.ProfitPrice = value - profitPriceDelta;
            }
            else
            {
                model.StopPrice = value - stopPriceDelta;
                model.ProfitPrice = value + profitPriceDelta;
            }

            return model;
        }

        protected virtual TradingModel GetNewShortTradingModel(double value, double bc)
        {
            var enterPriceDelta = IsCoefficient ? CalculatePrice(bc, MultyplayDelta) : ScopeDelta;
            var stopPriceDelta = IsCoefficient ? CalculatePrice(bc, MultyplayStop) : ScopeStop;
            var profitPriceDelta = IsCoefficient ? CalculatePrice(bc, MultyplayProfit) : ScopeProfit;

            var model = new TradingModel
            {
                Value = value,
                EnterPrice = value + enterPriceDelta
            };
            
            if (IsReverseMode)
            {
                model.StopPrice = value - stopPriceDelta;
                model.ProfitPrice = value + profitPriceDelta;
            }
            else
            {

                model.StopPrice = value + stopPriceDelta;
                model.ProfitPrice = value - profitPriceDelta;
            }

            return model;
        }
        
        protected TimeSpan GetTimeBeginBar()
        {
            return new TimeSpan(10, DataInterval - 1, 55);
        }
        
        protected TimeSpan GetTimeOneBar()
        {
            return new TimeSpan(0, DataInterval, 0);
        }
        
        protected double CalculatePrice(double bc, double baseLogarithm)
        {
            return Math.Round(Math.Log(bc / MultyplayDivider, baseLogarithm) / PriceStep, 0) * PriceStep;
        }
        
        public static PointModel MinByValue(PointModel[] source)
        {
            var min = source[0];
            var count = source.Length;
            
            for (var i = 1; i < count; i++)
            {
                if (source[i].Value < min.Value)
                {
                    min = source[i];
                }
            }

            return min;
        }
        
        public static PointModel MaxByValue(PointModel[] source)
        {
            var max = source[0];
            var count = source.Length;

            for (var i = 1; i < count; i++)
            {
                if (source[i].Value > max.Value)
                {
                    max = source[i];
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
