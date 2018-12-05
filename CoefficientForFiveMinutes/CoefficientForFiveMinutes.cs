// Decompiled with JetBrains decompiler
// Type: TSLabScripts.Simple
// Assembly: TSlabScripts, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null
// MVID: CAEE1DB7-E853-4F68-A074-937374B6C074
// Assembly location: C:\myProject\TSlabScripts\dll\TSlabScripts.dll

using MoreLinq;
using System;
using System.Collections.Generic;
using System.Linq;
using TSLab.DataSource;
using TSLab.Script;
using TSLab.Script.Handlers;
using TSLab.Script.Optimization;

namespace TSLabScripts
{
  public class Simple : IExternalScript, IExternalScriptBase, IStreamHandler, IHandler, IOneSourceHandler
  {
    public static OptimProperty DeltaModelSpanSeconds = new OptimProperty(0.0, 0.0, 86400.0, 5.0);
    public static OptimProperty DeltaPositionSpanSeconds = new OptimProperty(0.0, 0.0, 86400.0, 5.0);
    public OptimProperty Slippage = new OptimProperty(30.0, 0.0, 100.0, 10.0);
    public OptimProperty Value = new OptimProperty(1.0, 0.0, 100.0, 10.0);
    public OptimProperty LengthSegmentAB = new OptimProperty(0.0, 0.0, 5000.0, 10.0);
    public OptimProperty MinLengthSegmentBC = new OptimProperty(300.0, 0.0, 5000.0, 10.0);
    public OptimProperty MaxLengthSegmentBC = new OptimProperty(0.0, 0.0, 5000.0, 10.0);
    public OptimProperty MultyplayDelta = new OptimProperty(1.03, 1.0, 2.0, 1E-05);
    public OptimProperty MultyplayProfit = new OptimProperty(1011.0 / 1000.0, 1.0, 2.0, 1E-05);
    public OptimProperty MultyplayStop = new OptimProperty(1.0065, 1.0, 2.0, 1E-05);
    private TimeSpan TimeCloseAllPosition = new TimeSpan(18, 40, 0);
    private TimeSpan TimeBeginDayBar = new TimeSpan(10, 0, 0);
    private TimeSpan TimeBeginBar = new TimeSpan(10, 4, 55);
    private TimeSpan FiveSeconds = new TimeSpan(0, 0, 5);
    private TimeSpan FiveMinutes = new TimeSpan(0, 5, 0);
    private TimeSpan DeltaModelTimeSpan = new TimeSpan(0, 0, (int) Simple.DeltaModelSpanSeconds);
    private TimeSpan DeltaPositionTimeSpan = new TimeSpan(0, 0, (int) Simple.DeltaPositionSpanSeconds);

    public virtual void Execute(IContext ctx, ISecurity source)
    {
      if (!this.GetValidTimeFrame(ctx, source))
        return;
      ISecurity security = source.CompressTo(new Interval(5, DataIntervals.MINUTE), 0, 200, 0);
      IPane pane = ctx.CreatePane("Original", 70.0, false);
      pane.AddList(source.Symbol, security, CandleStyles.BAR_CANDLE, new Color(100, 100, 100), PaneSides.RIGHT);
      pane.AddList(source.Symbol, source, CandleStyles.BAR_CANDLE, new Color(0, 0, 0), PaneSides.RIGHT);
      List<double> buySignal = new List<double>();
      List<double> sellSignal = new List<double>();
      for (int index = 0; index < source.Bars.Count; ++index)
      {
        buySignal.Add(0.0);
        sellSignal.Add(0.0);
      }
      for (int actualBar = 1; actualBar <= source.Bars.Count - 1; ++actualBar)
        this.Trading(ctx, source, security, actualBar, buySignal, sellSignal);
      ctx.CreatePane("BuySignal", 15.0, false).AddList("BuySignal", (IList<double>) buySignal, ListStyles.HISTOHRAM_FILL, new Color(0, (int) byte.MaxValue, 0), LineStyles.SOLID, PaneSides.RIGHT);
      ctx.CreatePane("SellSignal", 15.0, false).AddList("SellSignal", (IList<double>) sellSignal, ListStyles.HISTOHRAM_FILL, new Color((int) byte.MaxValue, 0, 0), LineStyles.SOLID, PaneSides.RIGHT);
    }

    private void Trading(IContext ctx, ISecurity source, ISecurity compressSource, int actualBar, List<double> buySignal, List<double> sellSignal)
    {
      if (source.Bars[actualBar].Date.TimeOfDay < this.TimeBeginBar)
        return;
      if (source.Bars[actualBar].Date.TimeOfDay >= this.TimeCloseAllPosition)
      {
        if (source.Positions.ActivePositionCount <= 0)
          return;
        this.CloseAllPosition(source, actualBar);
      }
      else
      {
        if (source.Positions.ActivePositionCount > 0)
          this.SearchActivePosition(source, actualBar);
        if (Simple.IsClosedBar(source.Bars[actualBar]))
        {
          DateTime date = source.Bars[actualBar].Date;
          int indexBeginDayBar = this.GetIndexBeginDayBar(compressSource, date);
          int indexCompressBar = this.GetIndexCompressBar(compressSource, date, indexBeginDayBar);
          this.SearchBuyModel(ctx, compressSource, indexCompressBar, indexBeginDayBar, actualBar, buySignal);
          this.SearchSellModel(ctx, compressSource, indexCompressBar, indexBeginDayBar, actualBar, sellSignal);
        }
        List<Simple.TradingModel> tradingModelList1 = (List<Simple.TradingModel>) ctx.LoadObject("BuyModel") ?? new List<Simple.TradingModel>();
        if (tradingModelList1.Any<Simple.TradingModel>())
        {
          List<Simple.TradingModel> tradingModelList2 = this.ValidateBuyModel(source, tradingModelList1, actualBar);
          foreach (Simple.TradingModel tradingModel in tradingModelList2)
            source.Positions.BuyIfGreater(actualBar + 1, (double) this.Value, tradingModel.EnterPrice, new double?((double) this.Slippage), "buy_" + tradingModel.GetNamePosition);
          ctx.StoreObject("BuyModel", (object) tradingModelList2);
        }
        List<Simple.TradingModel> tradingModelList3 = (List<Simple.TradingModel>) ctx.LoadObject("SellModel") ?? new List<Simple.TradingModel>();
        if (!tradingModelList3.Any<Simple.TradingModel>())
          return;
        List<Simple.TradingModel> tradingModelList4 = this.ValidateSellModel(source, tradingModelList3, actualBar);
        foreach (Simple.TradingModel tradingModel in tradingModelList4)
          source.Positions.SellIfLess(actualBar + 1, (double) this.Value, tradingModel.EnterPrice, new double?((double) this.Slippage), "sell_" + tradingModel.GetNamePosition);
        ctx.StoreObject("SellList", (object) tradingModelList4);
      }
    }

    private void SearchBuyModel(IContext ctx, ISecurity compressSource, int indexCompressBar, int indexBeginDayBar, int actualBar, List<double> buySignal)
    {
      List<Simple.TradingModel> tradingModelList = new List<Simple.TradingModel>();
      for (int count = indexCompressBar - 1; count >= indexBeginDayBar && count >= 0; --count)
      {
        var data1 = compressSource.HighPrices.Select((value, index) => new
        {
          Value = value,
          Index = index
        }).Skip(count).Take(indexCompressBar - count + 1).MaxBy(item => item.Value);
        var data2 = compressSource.LowPrices.Select((value, index) => new
        {
          Value = value,
          Index = index
        }).Skip(count).Take(data1.Index - count + 1).MinBy(item => item.Value);
        if (data1.Index != data2.Index)
        {
          double num1 = data1.Value - data2.Value;
          if (num1 > (double) this.MinLengthSegmentBC && ((int) this.LengthSegmentAB == 0 || num1 < (double) this.LengthSegmentAB))
          {
            var data3 = compressSource.LowPrices.Select((value, index) => new
            {
              Value = value,
              Index = index
            }).Skip(data1.Index).Take(indexCompressBar - data1.Index + 1).MinBy(item => item.Value);
            if (data1.Index != data3.Index)
            {
              double bc = data1.Value - data3.Value;
              if (bc > (double) this.MinLengthSegmentBC && ((int) this.MaxLengthSegmentBC == 0 || bc < (double) this.MaxLengthSegmentBC) && data3.Value - data2.Value >= 0.0)
              {
                Simple.TradingModel newBuyTradingModel = this.GetNewBuyTradingModel(data1.Value, bc);
                if (indexCompressBar != data3.Index)
                {
                  double num2 = compressSource.HighPrices.Skip<double>(data3.Index + 1).Take<double>(indexCompressBar - data3.Index).Max();
                  if (newBuyTradingModel.EnterPrice <= num2)
                    continue;
                }
                if (!(this.DeltaModelTimeSpan != new TimeSpan(0, 0, 0)) || !(compressSource.Bars[indexCompressBar].Date - compressSource.Bars[data1.Index].Date > this.DeltaModelTimeSpan))
                {
                  tradingModelList.Add(newBuyTradingModel);
                  buySignal[actualBar] = 1.0;
                }
              }
            }
          }
        }
      }
      ctx.StoreObject("BuyModel", (object) tradingModelList);
    }

    private void SearchSellModel(IContext ctx, ISecurity compressSource, int indexCompressBar, int indexBeginDayBar, int actualBar, List<double> sellSignal)
    {
      List<Simple.TradingModel> tradingModelList = new List<Simple.TradingModel>();
      for (int count = indexCompressBar - 1; count >= indexBeginDayBar && count >= 0; --count)
      {
        var data1 = compressSource.LowPrices.Select((value, index) => new
        {
          Value = value,
          Index = index
        }).Skip(count).Take(indexCompressBar - count + 1).MinBy(item => item.Value);
        var data2 = compressSource.HighPrices.Select((value, index) => new
        {
          Value = value,
          Index = index
        }).Skip(count).Take(data1.Index - count + 1).MaxBy(item => item.Value);
        if (data1.Index != data2.Index)
        {
          double num1 = data2.Value - data1.Value;
          if (num1 > (double) this.MinLengthSegmentBC && ((int) this.LengthSegmentAB == 0 || num1 < (double) this.LengthSegmentAB))
          {
            var data3 = compressSource.HighPrices.Select((value, index) => new
            {
              Value = value,
              Index = index
            }).Skip(data1.Index).Take(indexCompressBar - data1.Index + 1).MaxBy(item => item.Value);
            if (data1.Index != data3.Index)
            {
              double bc = data3.Value - data1.Value;
              if (bc > (double) this.MinLengthSegmentBC && ((int) this.MaxLengthSegmentBC == 0 || bc < (double) this.MaxLengthSegmentBC) && data2.Value - data3.Value >= 0.0)
              {
                Simple.TradingModel sellTradingModel = this.GetNewSellTradingModel(data1.Value, bc);
                if (indexCompressBar != data3.Index)
                {
                  double num2 = compressSource.LowPrices.Skip<double>(data3.Index + 1).Take<double>(indexCompressBar - data3.Index).Min();
                  if (sellTradingModel.EnterPrice >= num2)
                    continue;
                }
                if (!(this.DeltaModelTimeSpan != new TimeSpan(0, 0, 0)) || !(compressSource.Bars[indexCompressBar].Date - compressSource.Bars[data1.Index].Date > this.DeltaModelTimeSpan))
                {
                  tradingModelList.Add(sellTradingModel);
                  sellSignal[actualBar] = 1.0;
                }
              }
            }
          }
        }
      }
      ctx.StoreObject("SellModel", (object) tradingModelList);
    }

    private List<Simple.TradingModel> ValidateBuyModel(ISecurity source, List<Simple.TradingModel> modelBuyList, int actualBar)
    {
      double lastMax = double.MinValue;
      for (int index = actualBar; index >= 0 && !Simple.IsClosedBar(source.Bars[index]); --index)
        lastMax = source.HighPrices[index] > lastMax ? source.HighPrices[index] : lastMax;
      return modelBuyList.Where<Simple.TradingModel>((Func<Simple.TradingModel, bool>) (model => model.EnterPrice > lastMax)).ToList<Simple.TradingModel>();
    }

    private List<Simple.TradingModel> ValidateSellModel(ISecurity source, List<Simple.TradingModel> modelSellList, int actualBar)
    {
      double lastMin = double.MaxValue;
      for (int index = actualBar; index >= 0 && !Simple.IsClosedBar(source.Bars[index]); --index)
        lastMin = source.LowPrices[index] < lastMin ? source.LowPrices[index] : lastMin;
      return modelSellList.Where<Simple.TradingModel>((Func<Simple.TradingModel, bool>) (model => model.EnterPrice < lastMin)).ToList<Simple.TradingModel>();
    }

    private void SearchActivePosition(ISecurity source, int actualBar)
    {
      foreach (IPosition position in source.Positions.GetActiveForBar(actualBar))
      {
        if (this.DeltaPositionTimeSpan != new TimeSpan(0, 0, 0) && source.Bars[actualBar].Date - position.EntryBar.Date >= this.DeltaPositionTimeSpan)
        {
          position.CloseAtMarket(actualBar + 1, "closeAtTime");
        }
        else
        {
          string[] strArray = position.EntrySignalName.Split('_');
          string str = strArray[0];
          if (!(str == "buy"))
          {
            if (str == "sell")
            {
              position.CloseAtStop(actualBar + 1, Convert.ToDouble(strArray[3]), new double?((double) this.Slippage), "closeStop");
              position.CloseAtProfit(actualBar + 1, Convert.ToDouble(strArray[4]), "closeProfit");
            }
          }
          else
          {
            position.CloseAtStop(actualBar + 1, Convert.ToDouble(strArray[3]), new double?((double) this.Slippage), "closeStop");
            position.CloseAtProfit(actualBar + 1, Convert.ToDouble(strArray[4]), "closeProfit");
          }
        }
      }
    }

    private void CloseAllPosition(ISecurity source, int actualBar)
    {
      foreach (IPosition position in source.Positions.GetActiveForBar(actualBar))
        position.CloseAtMarket(actualBar + 1, "closeAtTime");
    }

    private bool GetValidTimeFrame(IContext ctx, ISecurity source)
    {
      if (source.IntervalBase == DataIntervals.SECONDS && source.Interval == 5)
        return true;
      ctx.Log("Выбран не верный таймфрейм, выберите таймфрейм равный 5 секундам", new Color((int) byte.MaxValue, 0, 0), true);
      return false;
    }

    private int GetIndexCompressBar(ISecurity compressSource, DateTime dateActualBar, int indexBeginDayBar)
    {
      int index = indexBeginDayBar;
      DateTime dateTime = dateActualBar - this.FiveMinutes - this.FiveSeconds;
      while (compressSource.Bars[index].Date < dateTime)
        ++index;
      return index;
    }

    private int GetIndexBeginDayBar(ISecurity compressSource, DateTime dateActualBar)
    {
      while (true)
      {
        try
        {
          return compressSource.Bars.Select((bar, index) => new
          {
            Index = index,
            Bar = bar
          }).Last(item =>
          {
            if (item.Bar.Date.TimeOfDay == this.TimeBeginDayBar)
            {
              DateTime date = item.Bar.Date;
              if (date.Day == dateActualBar.Day)
              {
                date = item.Bar.Date;
                if (date.Month == dateActualBar.Month)
                {
                  date = item.Bar.Date;
                  return date.Year == dateActualBar.Year;
                }
              }
            }
            return false;
          }).Index;
        }
        catch (Exception ex)
        {
          this.TimeBeginDayBar = this.TimeBeginDayBar.Add(this.FiveMinutes);
        }
      }
    }

    private static bool IsClosedBar(Bar bar)
    {
      return (bar.Date.TimeOfDay.TotalSeconds + 5.0) % 300.0 == 0.0;
    }

    private Simple.TradingModel GetNewBuyTradingModel(double value, double bc)
    {
      return new Simple.TradingModel()
      {
        Value = value,
        EnterPrice = value - Math.Round(Math.Log(bc / 100.0, (double) this.MultyplayDelta) / 10.0, 0) * 10.0,
        StopPrice = value - Math.Round(Math.Log(bc / 100.0, (double) this.MultyplayStop) / 10.0, 0) * 10.0,
        ProfitPrice = value + Math.Round(Math.Log(bc / 100.0, (double) this.MultyplayProfit) / 10.0, 0) * 10.0
      };
    }

    private Simple.TradingModel GetNewSellTradingModel(double value, double bc)
    {
      return new Simple.TradingModel()
      {
        Value = value,
        EnterPrice = value + Math.Round(Math.Log(bc / 100.0, (double) this.MultyplayDelta) / 10.0, 0) * 10.0,
        StopPrice = value + Math.Round(Math.Log(bc / 100.0, (double) this.MultyplayStop) / 10.0, 0) * 10.0,
        ProfitPrice = value - Math.Round(Math.Log(bc / 100.0, (double) this.MultyplayProfit) / 10.0, 0) * 10.0
      };
    }

    private class TradingModel
    {
      public double Value { get; set; }

      public double EnterPrice { get; set; }

      public double StopPrice { get; set; }

      public double ProfitPrice { get; set; }

      public string GetNamePosition
      {
        get
        {
          return this.Value.ToString() + "_" + (object) this.EnterPrice + "_" + (object) this.StopPrice + "_" + (object) this.ProfitPrice;
        }
      }
    }
  }
}
