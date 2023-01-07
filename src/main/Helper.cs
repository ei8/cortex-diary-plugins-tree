using Blazored.Toast.Services;
using Microsoft.AspNetCore.Components;
using neurUL.Common.Domain.Model;
using neurUL.Common.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace ei8.Cortex.Diary.Plugins.Tree
{
    internal static class Helper
    {
        internal static void ReinitializeOption(Action<ContextMenuOption> optionSetter)
        {
            optionSetter(ContextMenuOption.NotSet);
            optionSetter(ContextMenuOption.New);
        }
    }
}
