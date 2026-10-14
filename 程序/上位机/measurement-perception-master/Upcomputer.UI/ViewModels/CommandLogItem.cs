using System;
using System.Globalization;

namespace Upcomputer.UI.ViewModels
{
    public class CommandLogItem
    {
        public CommandLogItem()
        {
        }

        public CommandLogItem(DateTime timestamp, string commandName, string commandDetails)
        {
            Timestamp = timestamp;
            CommandName = commandName;
            CommandDetails = commandDetails;
        }

        public DateTime Timestamp { get; set; }
        public string CommandName { get; set; } = string.Empty;
        public string CommandDetails { get; set; } = string.Empty;
        public string TimeDisplay => Timestamp.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
    }
}
