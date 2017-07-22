﻿using System;
using System.Collections.Generic;
using System.Linq;
using TSLab.DataSource;
using TSLab.Script;
using TSLab.Script.Handlers;

namespace TSlabScripts.Common
{
    public class SimpleHealper
    {
        public static void CloseAllPosition(ISecurity source, int actualBar)
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

        public static void InitModelIndicator(ISecurity source, IList<double> buySignal, IList<double> sellSignal)
        {
            buySignal = new List<double>();
            sellSignal = new List<double>();
            for (int i = 0; i < source.Bars.Count; i++)
            {
                buySignal.Add(0);
                sellSignal.Add(0);
            }
        }

        public static void RenderModelIndicator(IContext context, IList<double> buySignal, IList<double> sellSignal)
        {
            var buyPain = context.CreatePane("BuySignal", 15, false);
            buyPain.AddList("BuySignal", buySignal, ListStyles.HISTOHRAM_FILL, new Color(0, 255, 0), LineStyles.SOLID,
                PaneSides.RIGHT);

            var sellPain = context.CreatePane("SellSignal", 15, false);
            sellPain.AddList("SellSignal", sellSignal, ListStyles.HISTOHRAM_FILL, new Color(255, 0, 0), LineStyles.SOLID,
                PaneSides.RIGHT);
        }

        public static void RenderBars(IContext context, ISecurity source, ISecurity sourceCompress)
        {
            var pain = context.CreatePane("Original", 70, false);
            pain.AddList(source.Symbol, sourceCompress, CandleStyles.BAR_CANDLE, new Color(100, 100, 100), PaneSides.RIGHT);
            pain.AddList(source.Symbol, source, CandleStyles.BAR_CANDLE, new Color(0, 0, 0), PaneSides.RIGHT);
        }
    }
}
