using System;
using System.Collections.Generic;
using System.Linq;
using TSLab.DataSource;
using TSLab.Script;

namespace TSlabScripts.Common
{
    public class SimpleService
    {
        public static Point GetLowPrices(ISecurity source, int leftSide, int rigthSide)
        {
            return source.LowPrices.Select((value, index) => new Point { Value = value, Index = index }).
                    Skip(leftSide).
                    Take(rigthSide - leftSide + 1).
                    OrderBy(x => x.Value).ThenByDescending(x => x.Index).First();
        }

        public static Point GetHighPrices(ISecurity source, int leftSide, int rigthSide)
        {
            return source.HighPrices.Select((value, index) => new Point { Value = value, Index = index }).
                    Skip(leftSide).
                    Take(rigthSide - leftSide + 1).
                    OrderBy(x => x.Value).ThenBy(x => x.Index).Last();
        }

        public static int GetIndexActualCompressBar(ISecurity compressSource, DateTime dateActualBar, int indexBeginDayBar)
        {
            // Обязатеьно сравнивать по времени т.к. число баров может не соотвествовать
            var indexCompressBar = indexBeginDayBar;
            while (compressSource.Bars[indexCompressBar].Date < dateActualBar)
            {
                indexCompressBar++;
            }
            // -1 не разобрался зачем, но так работает
            return indexCompressBar - 1;
        }

        public static bool GetValidTimeFrame(DataIntervals intervalBase, int interval)
        {
            return intervalBase == DataIntervals.SECONDS && interval == 5;
        }

        public static bool IsStartFiveMinutesBar(ISecurity source, int actualBar)
        {
            return (source.Bars[actualBar].Date.TimeOfDay.TotalSeconds + 5) % 300 == 0;
        }

        public static int GetIndexBeginDayBar(ISecurity source, DateTime dateActualBar)
        {
            TimeSpan timeBeginDayBar = new TimeSpan(10, 00, 00);
            return source.Bars
                    .Select((bar, index) => new { Index = index, Bar = bar })
                    .Last(item =>
                    item.Bar.Date.TimeOfDay == timeBeginDayBar &&
                    item.Bar.Date.Day == dateActualBar.Day &&
                    item.Bar.Date.Month == dateActualBar.Month &&
                    item.Bar.Date.Year == dateActualBar.Year).Index;
        }
    }
}
