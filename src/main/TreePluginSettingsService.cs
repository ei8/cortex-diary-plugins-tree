using Microsoft.Extensions.Configuration;
using ei8.Cortex.Diary.Port.Adapter.UI.Views.Blazor.Common;

namespace ei8.Cortex.Diary.Plugins.Tree
{
    public class TreePluginSettingsService : IPluginSettingsService
    {
        private const int DefaultUpdateCheckInterval = 2000;

        public int UpdateCheckInterval => int.TryParse(this.Configuration["UpdateCheckInterval"], out int uci) ? uci : TreePluginSettingsService.DefaultUpdateCheckInterval;

        public IConfiguration Configuration { get; set; }
    }
}
