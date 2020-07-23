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
 *     (в 18:40).
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
using TSLab.Script.GraphPane;
using TSLab.Script.Handlers;
using TSLab.Script.Optimization;

namespace Simple
{
    public class Strategy : IExternalScript
    {
        public OptimProperty DataInterval = new OptimProperty(5, 1, 5, 1);
        public OptimProperty PriceStep = new OptimProperty(10, 0.001, 100, double.MaxValue);
        
        public OptimProperty MultyPosition = new OptimProperty(0, 0, 1, 1);

        public OptimProperty StartTime = new OptimProperty(100000, 100000, 240000, 1);
        public OptimProperty StopTime = new OptimProperty(180000, 100000, 240000, 1);
        
        public OptimProperty Value = new OptimProperty(1, 0, 1000, 1);
        public OptimProperty Slippage = new OptimProperty(30, 0, 1000, 0.01);
        public OptimProperty ScopeDelta = new OptimProperty(50, 0, 1000, 0.01);
        public OptimProperty ScopeProfit = new OptimProperty(100, 0, 10000, 0.01);
        public OptimProperty ScopeStop = new OptimProperty(300, 0, 10000, 0.01);
        public OptimProperty Rollback = new OptimProperty(100, 0, 1000, 0.01);
        
        public OptimProperty LengthSegmentAB = new OptimProperty(1000, 0, 10000, 0.01);
        public OptimProperty LengthSegmentBC = new OptimProperty(390, 0, 10000, 0.01);

        public OptimProperty EnableParabolic = new OptimProperty(0, 0, 1, 1);
        public OptimProperty AccelerationMax = new OptimProperty(0.02, 0.01, 1, 0.01);
        public OptimProperty AccelerationStart = new OptimProperty(0.02, 0.01, 1, 0.01);
        public OptimProperty AccelerationStep = new OptimProperty(0.02, 0.01, 1, 0.01);
        
        public OptimProperty EnableCoefficient = new OptimProperty(0, 0, 1, 1);
        public OptimProperty MultyplayDelta = new OptimProperty(1.03, 1.0, 2.0, double.MaxValue);
        public OptimProperty MultyplayProfit = new OptimProperty(1011.0 / 1000.0, 1.0, 2.0, double.MaxValue);
        public OptimProperty MultyplayStop = new OptimProperty(1.0065, 1.0, 2.0, double.MaxValue);
        public OptimProperty MultyplayDivider = new OptimProperty(10, 1.0, 1000, 10);
        
        public OptimProperty ShowModel = new OptimProperty(1, 0, 1, 1);
        public OptimProperty ShowEnterPrice = new OptimProperty(1, 0, 1, 1);
        public OptimProperty ShowRollback = new OptimProperty(1, 0, 1, 1);

        public TimeSpan FiveSeconds = new TimeSpan(0, 0, 5);
        public TimeSpan StartTimeTimeSpan;
        public TimeSpan StopTimeTimeSpan;
        public Boolean IsCoefficient;
        public Boolean IsParabolic;

        public string ShortModelKey = "ShortModel";
        public string LongModelKey = "LongModel";
        
        public Color CompressBarColor = new Color(100, 100, 100);
        public Color BarColor = new Color(255, 255, 255);
        public Color LongModelColor = new Color(0, 255, 0);
        public Color ShortModelColor = new Color(255, 0, 0);
        public Color ParabolicColor = new Color(255, 0, 0);
        public Color EnterPriceColor = new Color(255, 255, 0);
        public Color RollbackColor = new Color(153, 204, 255);
        
        public Random Random = new Random();

        public void Init(IContext ctx)
        {
            var startTimeString = StartTime.Value.ToString();
            StartTimeTimeSpan = new TimeSpan(
                Convert.ToInt32(startTimeString.Substring(0, 2)), 
                Convert.ToInt32(startTimeString.Substring(2, 2)), 
                Convert.ToInt32(startTimeString.Substring(4, 2)));

            var stopTimeString = StopTime.Value.ToString();
            StopTimeTimeSpan = new TimeSpan(
                Convert.ToInt32(stopTimeString.Substring(0, 2)), 
                Convert.ToInt32(stopTimeString.Substring(2, 2)), 
                Convert.ToInt32(stopTimeString.Substring(4, 2)));
            
            IsCoefficient = EnableCoefficient > 0;
            IsParabolic = EnableParabolic > 0;
            
            ctx.StoreObject(LongModelKey, new List<TradingModel>());
            ctx.StoreObject(ShortModelKey, new List<TradingModel>());
        }
        
        public void Execute(IContext ctx, ISecurity source)
        {
            Init(ctx);
            
            // Проверяем таймфрейм входных данных
            if (!GetValidTimeFrame(ctx, source)) return;

            // Компрессия исходного таймфрейма в пятиминутный
            var compressSource = source.CompressTo(new Interval(DataInterval, DataIntervals.MINUTE));

            // Генерация графика исходного таймфрейма
            var pain = ctx.CreatePane("Original", 90, false);

            pain.AddList(source.Symbol, compressSource, CandleStyles.BAR_CANDLE, CompressBarColor, PaneSides.RIGHT);
            pain.AddList(source.Symbol, source, CandleStyles.BAR_CANDLE, BarColor, PaneSides.RIGHT);

            var indicators = AddIndicatorOnMainPain(ctx, source, pain);
            
            for (var historyBar = 1; historyBar < source.Bars.Count; historyBar++)
            {
                Trading(pain, ctx, source, compressSource, historyBar, indicators);
            }
        }

        protected virtual Indicators AddIndicatorOnMainPain(IContext ctx, ISecurity source, IGraphPane pain)
        {
            var indicators = new Indicators();

            if (IsParabolic)
            {
                var parabolic = ctx.GetData("Parabolic", new[] {""}, () => new ParabolicSAR
                {
                    AccelerationMax = AccelerationMax,
                    AccelerationStart = AccelerationStart,
                    AccelerationStep = AccelerationStep
                }.Execute(source));
                var nameParabolic = "Parabolic (" + AccelerationStart.Value + "," + AccelerationStep.Value + "," +
                                    AccelerationMax.Value + ")";
                pain.AddList(nameParabolic, parabolic, ListStyles.LINE, ParabolicColor, LineStyles.SOLID,
                    PaneSides.RIGHT);

                indicators.Parabolic = parabolic;
            }

            return indicators;
        }

        private void Trading(
            IGraphPane pain, 
            IContext ctx,
            ISecurity source,
            ISecurity compressSource,
            int actualBar,
            Indicators indicators)
        {
            if (source.Bars[actualBar].Date.TimeOfDay < GetTimeBeginBar()) return;
            
            // Если время 18:40 или более - закрыть все активные позиции и не торговать
            if (source.Bars[actualBar].Date.TimeOfDay >= StopTimeTimeSpan)
            {
                ctx.StoreObject(LongModelKey, new List<TradingModel>());
                ctx.StoreObject(ShortModelKey, new List<TradingModel>());
                
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
                // Индекс бара на котором открылся день
                var indexBeginDayBar = GetIndexBeginDayBar(compressSource, dateActualBar);
                // Индекс последнего закрытого бара (в 5,3,1 интервале)
                var indexCompressBar = GetIndexCompressBar(compressSource, dateActualBar, indexBeginDayBar);

                // Поиск моделей, информация о моделях пишется в "StoreObject"
                // Модели ищутся только на открытии бара, а валидируются каждые 5 секунд

                var bars = compressSource.Bars
                    .Skip(indexBeginDayBar)
                    .Take(indexCompressBar - indexBeginDayBar + 1)
                    .ToList();
                
                var highPoints = bars
                    .Select((bar, index) => new PointModel
                    {
                        Date = bar.Date,
                        Value = bar.High,
                        Index = index
                    })
                    .ToArray();
                
                var lowPoints = bars
                    .Select((bar, index) => new PointModel
                    {
                        Date = bar.Date,
                        Value = bar.Low,
                        Index = index
                    })
                    .ToArray();
                
                SearchBuyModel(ctx, indexBeginDayBar, highPoints, lowPoints);
                SearchSellModel(ctx, indexBeginDayBar, highPoints, lowPoints);
            }
            
            if (ShowModel > 0)
            {
                PaintModel(pain, ctx, compressSource);
            }

            try
            {
                CreateOpenPositionOrder(pain, ctx, source, compressSource, actualBar, indicators);
            }
            catch (Exception e)
            {
                ctx.Log(e.ToString());
            }
        }

        private void PaintModel(IGraphPane pain, IContext ctx, ISecurity compressSource)
        {
            var modelBuyList = ctx.LoadObject(LongModelKey) as List<TradingModel> ?? new List<TradingModel>();
            var modelSellList = ctx.LoadObject(ShortModelKey) as List<TradingModel> ?? new List<TradingModel>();

            modelBuyList.ForEach(model =>
            {
                CreateLine(pain, compressSource, "AB" + model.GetNameForPaint, model.PointA, model.PointB, LongModelColor);
                CreateLine(pain, compressSource, "BC" + model.GetNameForPaint, model.PointB, model.PointC, LongModelColor);
                CreateLine(pain, compressSource, "AC" + model.GetNameForPaint, model.PointA, model.PointC, LongModelColor);
            });

            modelSellList.ForEach(model =>
            {
                CreateLine(pain, compressSource, "AB" + model.GetNameForPaint, model.PointA, model.PointB, ShortModelColor);
                CreateLine(pain, compressSource, "BC" + model.GetNameForPaint, model.PointB, model.PointC, ShortModelColor);
                CreateLine(pain, compressSource, "AC" + model.GetNameForPaint, model.PointA, model.PointC, ShortModelColor);
            });
        }

        private void CreateLine(IGraphPane pain,
            ISecurity compressSource,
            string id,
            PointModel firstPoint,
            PointModel secondPoint, 
            Color color)
        {
            var isExist = pain.GetInteractiveObjects().Any(x => x.Id == id);
            var halfBar = new TimeSpan(0, DataInterval / 2, 0);

            if (!isExist)
            {
                pain.AddInteractiveLine(
                    id,
                    PaneSides.RIGHT,
                    false,
                    color,
                    InteractiveLineMode.Finite,
                    new MarketPoint(compressSource.Bars[firstPoint.Index].Date.Add(halfBar), firstPoint.Value),
                    new MarketPoint(compressSource.Bars[secondPoint.Index].Date.Add(halfBar), secondPoint.Value));
            }
        }

        private void CreateOpenPositionOrder(
            IGraphPane pain, 
            IContext ctx, 
            ISecurity source, 
            ISecurity compressSource,
            int actualBar,
            Indicators indicators)
        {
            var timeActualBar = source.Bars[actualBar].Date.TimeOfDay;
            var canOpenPosition = (MultyPosition > 0 || source.Positions.ActivePositionCount == 0)
                                  && timeActualBar > StartTimeTimeSpan && timeActualBar < StopTimeTimeSpan;

            var modelBuyList = (ctx.LoadObject(LongModelKey) ?? new List<TradingModel>()) as List<TradingModel>;
            var modelSellList = (ctx.LoadObject(ShortModelKey) ?? new List<TradingModel>()) as List<TradingModel>;

            if (modelBuyList != null && modelBuyList.Any())
            {
                var buyModels = ValidateBuyModel(source, modelBuyList, actualBar);

                if (canOpenPosition)
                {
                    foreach (var model in buyModels.ValidModel)
                    {
                        PaintEnterPrice(pain, source, compressSource, actualBar, model); 
                        PaintRollbackPrice(pain, source, compressSource, actualBar, model); 
                    }
                    
                    foreach (var model in buyModels.ValidModelToSetPosition)
                    {
                        CreateLongOrder(source, actualBar, model, indicators);
                    }
                }

                ctx.StoreObject(LongModelKey, buyModels.ValidModel);
            }

            if (modelSellList != null && modelSellList.Any())
            {
                var sellModels = ValidateSellModel(source, modelSellList, actualBar);

                if (canOpenPosition)
                {
                    foreach (var model in sellModels.ValidModel)
                    {
                        PaintEnterPrice(pain, source, compressSource, actualBar, model); 
                        PaintRollbackPrice(pain, source, compressSource, actualBar, model); 
                    }
                    
                    foreach (var model in sellModels.ValidModelToSetPosition)
                    {
                        CreateShortOrder(source, actualBar, model, indicators);
                    }
                }

                ctx.StoreObject(ShortModelKey, sellModels.ValidModel);
            }
        }

        private void PaintEnterPrice(IGraphPane pain, ISecurity source, ISecurity compressSource, int actualBar,
            TradingModel model)
        {
            if (ShowEnterPrice == 0)
            {
                return;
            }
            
            var id = "EnterPrice" + model.GetNameForPaint;
            var isExist = pain.GetInteractiveObjects().Any(x => x.Id == id);

            if (isExist)
            {
                pain.RemoveInteractiveObject(id);
            }

            var endPoint = model.EnterPriceBarIndex != 0 ? model.EnterPriceBarIndex : actualBar;
            
            pain.AddInteractiveLine(
                id,
            PaneSides.RIGHT,
            false,
            EnterPriceColor,
            InteractiveLineMode.Finite,
            new MarketPoint(compressSource.Bars[model.PointC.Index + 1].Date, model.EnterPrice),
            new MarketPoint(source.Bars[endPoint].Date, model.EnterPrice)
            );
        }

        private void PaintRollbackPrice(IGraphPane pain, ISecurity source, ISecurity compressSource, int actualBar,
            TradingModel model)
        {
            if (ShowRollback == 0)
            {
                return;
            }
            
            if (model.EnterPriceBarIndex == 0)
            {
                return;
            }
            
            var id = "RollbackPrice" + model.GetNameForPaint;
            var isExist = pain.GetInteractiveObjects().Any(x => x.Id == id);

            if (isExist)
            {
                pain.RemoveInteractiveObject(id);
            }

            pain.AddInteractiveLine(
                id,
                PaneSides.RIGHT,
                false,
                RollbackColor,
                InteractiveLineMode.Finite,
                new MarketPoint(source.Bars[model.EnterPriceBarIndex].Date, model.RollbackPrice),
                new MarketPoint(source.Bars[actualBar].Date, model.RollbackPrice)
            );
        }

        public virtual void CreateLongOrder(ISecurity source, int actualBar, TradingModel model, Indicators indicators)
        {
            if (IsParabolic)
            {
                var parabolicValue = indicators.Parabolic[actualBar];
                if (parabolicValue > model.EnterPrice)
                {
                    return;
                }
            }

            source.Positions.BuyIfLess(
                    actualBar + 1,
                    Value, model.RollbackPrice,
                    Slippage,
                    "buy_" + model.GetNamePosition);
        }

        public virtual void CreateShortOrder(ISecurity source, int actualBar, TradingModel model, Indicators indicators)
        {
            if (IsParabolic)
            {
                var parabolicValue = indicators.Parabolic[actualBar];
                if (parabolicValue < model.EnterPrice)
                {
                    return;
                }
            }

            source.Positions.SellIfGreater(
                    actualBar + 1, 
                    Value, model.RollbackPrice, 
                    Slippage, 
                    "sell_" + model.GetNamePosition);
        }

        private void SearchBuyModel(IContext ctx,
            int indexBeginDayBar,
            IReadOnlyList<PointModel> highPoints,
            IReadOnlyList<PointModel> lowPoints)
        {
            var models = ctx.LoadObject(LongModelKey) as List<TradingModel> ?? new List<TradingModel>();
            var lastBar = highPoints.Count - 1;
            for (var indexPointA = lastBar - 1; indexPointA >= 0; indexPointA--)
            {
                var pointB = highPoints
                    .Skip(indexPointA)
                    .MaxByValue();

                var realPointA = lowPoints
                    .Skip(indexPointA)
                    .Take(pointB.Index - indexPointA + 1)
                    .MinByValue();
                
                // Точки A и B не могут быть на одном баре
                if (pointB.Index == realPointA.Index) continue;

                // Проверм размер фигуры A-B
                var ab = pointB.Value - realPointA.Value;
                if (ab <= LengthSegmentBC || ab >= LengthSegmentAB) continue;

                var pointC = lowPoints
                    .Skip(pointB.Index)
                    .MinByValue();
                
                // Точки B и C не могут быть на одном баре
                if (pointB.Index == pointC.Index) continue;

                // Проверям размер модели B-C
                var bc = pointB.Value - pointC.Value;
                if (bc <= LengthSegmentBC || pointC.Value - realPointA.Value < 0) continue;

                if(IsExistModel(models, pointB, pointC, indexBeginDayBar)) continue;

                var model = GetNewLongTradingModel(realPointA, pointB, pointC);
                
                // Проверяем, не пересекала ли модель ProfitPrice и StopPrice
                if (lastBar != pointC.Index)
                {
                    var validateMax = highPoints
                        .Skip(pointC.Index + 1)
                        .Max(x => x.Value);
                    if (validateMax >= model.ProfitPrice) continue;
                    
                    // Грубо фильтруем модели что пробили EnterPrice после чего откатились до Rollback
                    if (validateMax >= model.EnterPrice)
                    {
                        var enterPriceBar = GetLongEnterPriceBar(pointC.Index + 1, highPoints, model);
                        if (enterPriceBar != null)
                        {
                            var rollbackPriceBar = GetLongRollbackPriceBar((int) enterPriceBar + 1, highPoints, model);
                            if (rollbackPriceBar != null) continue;
                        }
                    }
                }
                
                model.PointA.Index += indexBeginDayBar;
                model.PointB.Index += indexBeginDayBar;
                model.PointC.Index += indexBeginDayBar;
                
                models.Add(model);
            }

            ctx.StoreObject(LongModelKey, models);
        }

        private static int? GetLongEnterPriceBar(
            int startIndex, 
            IEnumerable<PointModel> points, 
            TradingModel model)
        {
            return points
                .Skip(startIndex + 1)
                .FirstOrDefault(x => x.Value >= model.EnterPrice)?.Index;
        }
        
        private static int? GetLongRollbackPriceBar(
            int startIndex, 
            IEnumerable<PointModel> points, 
            TradingModel model)
        {
            return points
                .Skip(startIndex + 1)
                .FirstOrDefault(x => x.Value <= model.RollbackPrice)?.Index;
        }

        private void SearchSellModel(IContext ctx,
            int indexBeginDayBar,
            IReadOnlyList<PointModel> highPoints,
            IReadOnlyList<PointModel> lowPoints)
        {
            var models = ctx.LoadObject(ShortModelKey) as List<TradingModel> ?? new List<TradingModel>();
            var lastBar = highPoints.Count - 1;
            for (var indexPointA = lastBar - 1; indexPointA >= 0; indexPointA--)
            {
                var pointB = lowPoints
                    .Skip(indexPointA)
                    .MinByValue();

                var realPointA = highPoints
                    .Skip(indexPointA)
                    .Take(pointB.Index - indexPointA + 1)
                    .MaxByValue();

                // Точки A и B не могут быть на одном баре
                if (pointB.Index == realPointA.Index) continue;

                // Проверм размер фигуры A-B
                var ab = realPointA.Value - pointB.Value;
                if (ab <= LengthSegmentBC || ab >= LengthSegmentAB) continue;

                var pointC = highPoints
                    .Skip(pointB.Index)
                    .MaxByValue();

                // Точки B и C не могут быть на одном баре
                if (pointB.Index == pointC.Index) continue;

                // Проверям размер модели B-C
                var bc = pointC.Value - pointB.Value;
                if (bc <= LengthSegmentBC || realPointA.Value - pointC.Value < 0) continue;
                
                if(IsExistModel(models, pointB, pointC, indexBeginDayBar)) continue;

                var model = GetNewShortTradingModel(realPointA, pointB, pointC);

                // Проверяем, не пересекала ли модель ProfitPrice и StopPrice
                if (lastBar != pointC.Index)
                {
                    var validateMin = lowPoints
                        .Skip(pointC.Index + 1)
                        .Min(x => x.Value);
                    if (validateMin <= model.ProfitPrice) continue;
                    
                    // Грубо фильтруем модели что пробили EnterPrice после чего откатились до Rollback
                    if (validateMin <= model.EnterPrice)
                    {
                        var enterPriceBar = GetShortEnterPriceBar(pointC.Index + 1, lowPoints, model);
                        if (enterPriceBar != null)
                        {
                            var rollbackPriceBar = GetShortRollbackPriceBar((int) enterPriceBar + 1, lowPoints, model);
                            if (rollbackPriceBar != null) continue;
                        }
                    }
                }

                model.PointA.Index += indexBeginDayBar;
                model.PointB.Index += indexBeginDayBar;
                model.PointC.Index += indexBeginDayBar;
                
                models.Add(model);
            }

            ctx.StoreObject(ShortModelKey, models);
        }
        
        private static int? GetShortEnterPriceBar(
            int startIndex, 
            IEnumerable<PointModel> points, 
            TradingModel model)
        {
            var point = points.Skip(startIndex).FirstOrDefault(x => x.Value <= model.EnterPrice);
            return point?.Index;
        }
        
        private static int? GetShortRollbackPriceBar(
            int startIndex, 
            IEnumerable<PointModel> points, 
            TradingModel model)
        {
            var point = points.Skip(startIndex).FirstOrDefault(x => x.Value >= model.RollbackPrice);
            return point?.Index;
        }

        private ValidationResult ValidateBuyModel(
            ISecurity source,
            IEnumerable<TradingModel> modelBuyList,
            int actualBar)
        {
            var models = new ValidationResult
            {
                ValidModel = new List<TradingModel>(),
                ValidModelToSetPosition = new List<TradingModel>()
            };
            
            foreach (var tradingModel in modelBuyList)
            {
                var firstBarDate = tradingModel.PointC.Date.Add(GetTimeOneBar());
                var indexBar = source.Bars.GetBarIndexForDate(firstBarDate);
                
                if (indexBar >= actualBar)
                {
                    models.ValidModel.Add(tradingModel);
                    continue;
                }
                
                var validateMin = source.LowPrices.Skip(indexBar).Take(actualBar - indexBar).Min();
                if (validateMin <= tradingModel.PointC.Value) continue;

                var validateMax = source.HighPrices.Skip(indexBar).Take(actualBar - indexBar).Max();
                if (validateMax >= tradingModel.ProfitPrice) continue;

                var enterPriceBar = GetBuyEnterPriceBar(indexBar, actualBar, source, tradingModel);
                if (enterPriceBar == null)
                {
                    models.ValidModel.Add(tradingModel);
                    continue;
                }
                
                var rollbackPriceBar = GetBuyRollbackPriceBar((int)enterPriceBar + 1, actualBar, source, tradingModel);
                if (rollbackPriceBar == null)
                {
                    tradingModel.EnterPriceBarIndex = (int) enterPriceBar;
                    models.ValidModel.Add(tradingModel);
                    models.ValidModelToSetPosition.Add(tradingModel);
                }
            }

            return models;
        }

        private int? GetBuyEnterPriceBar(int indexBar, int actualBar, ISecurity source, TradingModel tradingModel)
        {
            for (; indexBar <= actualBar; indexBar++)
            {
                if (source.Bars[indexBar].High >= tradingModel.EnterPrice)
                {
                    return indexBar;
                }
            }

            return null;
        }

        private int? GetBuyRollbackPriceBar(int indexBar, int actualBar, ISecurity source, TradingModel tradingModel)
        {
            for (; indexBar <= actualBar; indexBar++)
            {
                if (source.Bars[indexBar].Low <= tradingModel.RollbackPrice)
                {
                    return indexBar;
                }
            }

            return null;
        }

        private ValidationResult ValidateSellModel(
            ISecurity source, 
            IReadOnlyCollection<TradingModel> modelSellList, 
            int actualBar)
        {
            var models = new ValidationResult
            {
                ValidModel = new List<TradingModel>(),
                ValidModelToSetPosition = new List<TradingModel>()
            };
            
            foreach (var tradingModel in modelSellList)
            {
                var firstBarDate = tradingModel.PointC.Date.Add(GetTimeOneBar());
                var indexBar = source.Bars.GetBarIndexForDate(firstBarDate);

                if (indexBar >= actualBar)
                {
                    models.ValidModel.Add(tradingModel);
                    continue;
                }
                
                var validateMax = source.HighPrices.Skip(indexBar).Take(actualBar - indexBar).Max();
                if (validateMax >= tradingModel.PointC.Value) continue;

                var validateMin = source.LowPrices.Skip(indexBar).Take(actualBar - indexBar).Min();
                if (validateMin <= tradingModel.ProfitPrice) continue;

                var enterPriceBar = GetSellEnterPriceBar(indexBar, actualBar, source, tradingModel);
                if (enterPriceBar == null)
                {
                    models.ValidModel.Add(tradingModel);
                    continue;
                }
                
                var rollbackPriceBar = GetSellRollbackPriceBar((int)enterPriceBar, actualBar, source, tradingModel);
                if (rollbackPriceBar == null)
                {
                    tradingModel.EnterPriceBarIndex = (int) enterPriceBar;
                    models.ValidModel.Add(tradingModel);
                    models.ValidModelToSetPosition.Add(tradingModel);
                }
            }

            return models;
        }

        private int? GetSellEnterPriceBar(int indexBar, int actualBar, ISecurity source, TradingModel tradingModel)
        {
            for (var i = indexBar; i <= actualBar; i++)
            {
                if (source.Bars[i].Low <= tradingModel.EnterPrice)
                {
                    return i;
                }
            }

            return null;
        }

        private int? GetSellRollbackPriceBar(int indexBar, int actualBar, ISecurity source, TradingModel tradingModel)
        {
            for (var i = indexBar; i <= actualBar; i++)
            {
                if (source.Bars[i].High >= tradingModel.RollbackPrice)
                {
                    return i;
                }
            }

            return null;
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

            if (IsParabolic)
            {
                prises.Add(Convert.ToDouble(indicators.Parabolic[actualBar]));
            }

            position.CloseAtStop(actualBar + 1, prises.Max(), Convert.ToDouble(Slippage), "closeStop");
        }
        
        protected virtual void SetShortStop(int actualBar, IPosition position, string[] arr, Indicators indicators)
        {
            var prises = new List<double>{ Convert.ToDouble(arr[3]) };

            if (IsParabolic)
            {
                prises.Add(Convert.ToDouble(indicators.Parabolic[actualBar]));
            }

            position.CloseAtStop(actualBar + 1, prises.Min(), Convert.ToDouble(Slippage), "closeStop");
        }
        
        protected virtual void SetLongProfit(int actualBar, IPosition position, string[] arr, Indicators indicators)
        {
            position.CloseAtProfit(actualBar + 1, Convert.ToDouble(arr[4]), "closeProfit");
        }
        
        protected virtual void SetShortProfit(int actualBar, IPosition position, string[] arr, Indicators indicators)
        {
            position.CloseAtProfit(actualBar + 1, Convert.ToDouble(arr[4]), "closeProfit");
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

        protected virtual TradingModel GetNewLongTradingModel(
            PointModel pointA, 
            PointModel pointB,
            PointModel pointC)
        {
            var bc = pointB.Value - pointC.Value;
            
            var enterPriceDelta = IsCoefficient ? CalculatePrice(bc, MultyplayDelta) : ScopeDelta;
            var stopPriceDelta = IsCoefficient ? CalculatePrice(bc, MultyplayStop) : ScopeStop;
            var profitPriceDelta = IsCoefficient ? CalculatePrice(bc, MultyplayProfit) : ScopeProfit;

            var enterPrice = pointB.Value - enterPriceDelta;
            return new TradingModel
            {
                PointA = pointA.Clone(),
                PointB = pointB.Clone(),
                PointC = pointC.Clone(),
                
                Value = pointB.Value,
                EnterPrice = pointB.Value - enterPriceDelta,
                RollbackPrice = enterPrice - Rollback,
                StopPrice = pointB.Value - stopPriceDelta,
                ProfitPrice = pointB.Value + profitPriceDelta
            };
        }

        protected virtual TradingModel GetNewShortTradingModel(
            PointModel pointA, 
            PointModel pointB,
            PointModel pointC)
        {
            var bc = pointC.Value - pointB.Value;
            
            var enterPriceDelta = IsCoefficient ? CalculatePrice(bc, MultyplayDelta) : ScopeDelta;
            var stopPriceDelta = IsCoefficient ? CalculatePrice(bc, MultyplayStop) : ScopeStop;
            var profitPriceDelta = IsCoefficient ? CalculatePrice(bc, MultyplayProfit) : ScopeProfit;

            var enterPrice = pointB.Value + enterPriceDelta;
            return new TradingModel
            {
                PointA = pointA.Clone(),
                PointB = pointB.Clone(),
                PointC = pointC.Clone(),
                
                Value = pointB.Value,
                EnterPrice = pointB.Value + enterPriceDelta,
                RollbackPrice = enterPrice + Rollback,
                StopPrice = pointB.Value + stopPriceDelta,
                ProfitPrice = pointB.Value - profitPriceDelta
            };
        }
        
        private static bool IsExistModel(
            IEnumerable<TradingModel> models, 
            PointModel pointB,
            PointModel pointC, 
            int indexBeginDayBar)
        {
            return models.Any(x => 
                x.PointB.Index - indexBeginDayBar == pointB.Index
                && x.PointC.Index - indexBeginDayBar == pointC.Index);
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
    }

    public class TradingModel
    {
        public PointModel PointA { get; set; }
        
        public PointModel PointB { get; set; }
        
        public PointModel PointC { get; set; }
        
        public double Value { get; set; }
        
        public double EnterPrice { get; set; }
        
        public int EnterPriceBarIndex { get; set; }
        
        public double RollbackPrice { get; set; }

        public double StopPrice { get; set; }

        public double ProfitPrice { get; set; }
        
        public string GetNameForPaint
        {
            get { return PointA.Date + "/" + PointA.Value + "_" + PointB.Date + "/" + PointB.Value + "_" + PointC.Date + "/" + PointC.Value; }
        }

        public string GetNamePosition
        {
            get { return Value + "_" + EnterPrice + "_" + StopPrice + "_" + ProfitPrice; }
        }
    }
    
    public class PointModel
    {
        public DateTime Date { get; set; }
        
        public double Value { get; set; }
        
        public int Index { get; set; }

        public PointModel Clone()
        {
            return new PointModel
            {
                Date = Date,
                Value = Value,
                Index = Index
            };
        }
    }
    
    public class Indicators
    {
        public IList<double> Parabolic { get; set; }
    }

    public class ValidationResult
    {
        public List<TradingModel> ValidModel { get; set; }
        
        public List<TradingModel> ValidModelToSetPosition { get; set; }
    }

    public static class Extension
    {
        public static PointModel MaxByValue(this IEnumerable<PointModel> source)
        {
            var models = source.ToArray();
            
            var max = models[0];
            var count = models.Length;

            for (var i = 1; i < count; i++)
            {
                if (models[i].Value > max.Value)
                {
                    max = models[i];
                }
            }

            return max;
        }
        
        public static PointModel MinByValue(this IEnumerable<PointModel> source)
        {
            var models = source.ToArray();
            
            var min = models[0];
            var count = models.Length;
            
            for (var i = 1; i < count; i++)
            {
                if (models[i].Value < min.Value)
                {
                    min = models[i];
                }
            }

            return min;
        }
    }
}
