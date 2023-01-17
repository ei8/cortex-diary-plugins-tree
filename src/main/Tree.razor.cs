using Blazored.Toast.Services;
using Blazorise;
using ei8.Cortex.Diary.Application.Identity;
using ei8.Cortex.Diary.Application.Neurons;
using ei8.Cortex.Diary.Application.Settings;
using ei8.Cortex.Diary.Application.Subscriptions;
using ei8.Cortex.Diary.Port.Adapter.UI.ViewModels;
using ei8.Cortex.Diary.Port.Adapter.UI.Views.Blazor.Common;
using ei8.Cortex.Diary.Port.Adapter.UI.Views.Common;
using ei8.Cortex.Library.Client;
using ei8.Cortex.Library.Common;
using ei8.Cortex.Subscriptions.Common;
using ei8.Cortex.Subscriptions.Common.Receivers;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.JSInterop;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;

namespace ei8.Cortex.Diary.Plugins.Tree
{
    public partial class Tree : ComponentBase, IDefaultComponentParameters, IDisposable
    {
        private bool reloading = true;
        private Dropdown optionsDropdown;
        private Timer refreshTimer;
        private DotNetObjectReference<Tree>? dotNetHelper;
        private TreePluginSettingsService pluginSettingsService;

        public Tree()
        {
            this.internalSelectedOptionChanged = EventCallback.Factory.Create(this, new Func<ContextMenuOption, Task>(this.HandleSelectionOptionChanged));
        }

        private async Task HandleSelectionOptionChanged(ContextMenuOption value)
        {
            switch (value)
            {
                case ContextMenuOption.New:
                    this.SelectedNeuron = null;
                    this.EditNeuron = null;
                    this.ControlsEnabled = true;
                    break;
                case ContextMenuOption.Delete:
                    this.IsConfirmVisible = true;
                    break;
                case ContextMenuOption.Edit:
                case ContextMenuOption.AddRelative:
                    this.ControlsEnabled = false;
                    this.EditNeuron = this.SelectedNeuron.Neuron;
                    break;
            }
        }

        private async void OnKeyPress(KeyboardEventArgs e)
        {
            if (e.Code == "Enter" || e.Code == "NumpadEnter")
            {
                if (!string.IsNullOrEmpty(this.AvatarUrl))
                    await this.Reload();
            }
        }

        private void ShowOptionsMenu()
        {
            if (this.optionsDropdown.Visible)
                this.optionsDropdown.Hide();
            else
                this.optionsDropdown.Show();

            // hide other dropdowns
            //this.userDropdown.Hide();
        }

        private void ShowAvatarEditorBox()
        {
            this.IsAvatarUrlVisible = true;
        }

        private void ToggleRenderDirection()
        {
            this.RenderDirection = this.RenderDirection == RenderDirectionValue.TopToBottom ?
                RenderDirectionValue.BottomToTop :
                RenderDirectionValue.TopToBottom;

            this.optionsDropdown.Hide();
        }

        [JSInvokable]
        public async Task<Boolean> SendLinkData(string sourceNeuronID, string targetNeuronID)
        {
            var result = false;
            await this.ToastService.UITryHandler(
                async () =>
                {
                    if (QueryUrl.TryParse(this.AvatarUrl, out QueryUrl resultQuery))
                    {
                        await this.TerminalApplicationService.AddLink(resultQuery.AvatarUrl, sourceNeuronID, targetNeuronID);
                    }

                    return true;
                },
                () => "Linking"
            );

            return result;
        }

        private void SearchDefaultRegionNeuron()
        {
            this.IsSearchDefaultRegionNeuronVisible = true;

            this.optionsDropdown.Hide();
        }

        protected override void OnInitialized()
        {
            this.Children = new List<TreeNeuronViewModel>();
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
            {
                var uri = this.NavigationManager.ToAbsoluteUri(this.NavigationManager.Uri);
                if (QueryHelpers.ParseQuery(uri.Query).TryGetValue("direction", out var directionValue) &&
                    Enum.TryParse<RenderDirectionValue>(directionValue, out RenderDirectionValue directionEnum)
                    )
                {
                    this.RenderDirection = directionEnum;
                    this.StateHasChanged();
                }
                bool urlSet = false;
                var query = QueryHelpers.ParseQuery(uri.Query);
                if (query.TryGetValue("avatarUrl", out var encodedAvatarUrl))
                {
                    Uri uriResult;
                    string decodedUrl = Nancy.Helpers.HttpUtility.UrlDecode(encodedAvatarUrl);
                    bool validUrl = Uri.TryCreate(decodedUrl, UriKind.Absolute, out uriResult)
                        && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);
                    if (validUrl)
                    {
                        urlSet = true;
                        Neuron regionNeuron = null;
                        IEnumerable<Neuron> postsynapticNeurons = null;

                        if (Library.Client.QueryUrl.TryParse(decodedUrl, out QueryUrl urlResult))
                        {
                            if (query.TryGetValue("regionId", out var regionId))
                            {
                                regionNeuron = (await this.NeuronQueryService.GetNeuronById(
                                    urlResult.AvatarUrl,
                                    regionId.ToString(),
                                    new NeuronQuery()
                                    ))?.Items.SingleOrDefault();
                            }

                            if (query.TryGetValue("postsynaptic", out var postsynaptics))
                            {
                                postsynapticNeurons = (await this.NeuronQueryService.GetNeurons(
                                    urlResult.AvatarUrl,
                                    new NeuronQuery()
                                    {
                                        Id = postsynaptics.ToArray()
                                    }
                                    ))?.Items;
                            }
                        }

                        await Task.Run(() =>
                        {
                            this.AvatarUrl = decodedUrl;
                            this.InitialRegionNeuron = regionNeuron;
                            this.InitialPostsynapticNeurons = postsynapticNeurons;
                            this.Reload();
                        });

                        Helper.ReinitializeOption(o => this.SelectedOption = o);
                    }
                }
                if (!urlSet)
                    await this.SetReloading(false);

                this.refreshTimer = new Timer();
                this.refreshTimer.Interval = this.pluginSettingsService.UpdateCheckInterval;
                this.refreshTimer.Elapsed += OnTimerInterval;
                this.refreshTimer.AutoReset = true;
                // Start the timer
                this.refreshTimer.Enabled = true;

                // register dotnetHelper
                this.dotNetHelper = DotNetObjectReference.Create(this);
                await this.JsRuntime.InvokeAsync<string>("DotNetHelperSetter", this.dotNetHelper);
                // register push notification service worker
                var objRef = DotNetObjectReference.Create(this);
                await this.JsRuntime.InvokeVoidAsync("RegisterServiceWorker", objRef);
            }
            await base.OnAfterRenderAsync(firstRender);
        }

        private async void OnTimerInterval(object sender, ElapsedEventArgs e)
        {
            if (this.Children.Count() > 0)
            {
                try
                {
                    var ns = await Tree.GetOrderedNeurons(this);
                    var currentLastIndex = ns.ToList().FindLastIndex(nr => nr.Id == this.Children.Last().Neuron.Id);
                    var newNeurons = ns.Where((n, i) => i > currentLastIndex && !this.Children.Any(nvm => nvm.Neuron.Id == n.Id));
                    if (newNeurons.Count() > 0)
                    {
                        if (this.NewItemsCount == 0)
                            await this.JsRuntime.InvokeAsync<string>("PlaySound");

                        this.NewItemsCount += newNeurons.Count();
                        newNeurons.ToList().ForEach(n => this.Children.Add(new TreeNeuronViewModel(new Neuron(n), this.AvatarUrl, this.NeuronQueryService)));
                        await this.InvokeAsync(() => this.StateHasChanged());
                    }
                }
                catch (Exception ex)
                {
                    this.ToastService.ShowError(ex.ToString());
                }
            }
        }

        public void Dispose()
        {
            // During prerender, this component is rendered without calling OnAfterRender and then immediately disposed
            // this mean timer will be null so we have to check for null or use the Null-conditional operator ?
            this.refreshTimer?.Dispose();
        }

        private async static Task<IEnumerable<Neuron>> GetOrderedNeurons(Tree value)
        {
            var ns = (await value.NeuronQueryService.SendQuery(
                        value.AvatarUrl,
                        Helpers.String.IsExternalUrl(value.NavigationManager.Uri, value.AvatarUrl)
                        )).Items;

            if (value.RenderDirection == RenderDirectionValue.BottomToTop)
                ns = ns.Reverse().ToArray();

            return ns;
        }

        private int NewItemsCount { get; set; } = 0;

        public string IdServerUrl { get; set; }

        [Parameter]
        public string AvatarUrl { get; set; }

        [Parameter]
        public IList<TreeNeuronViewModel> Children { get; set; } = new List<TreeNeuronViewModel>();

        private bool IsConfirmVisible { get; set; } = false;

        private bool IsContextMenuVisible { get; set; } = false;

        private bool IsInfoVisible { get; set; } = false;

        private bool IsAvatarUrlVisible { get; set; } = false;

        private RenderDirectionValue RenderDirection { get; set; } = RenderDirectionValue.TopToBottom;

        private NeuronResultItemViewModel selectedDefaultRegionNeuron;
        private NeuronResultItemViewModel SelectedDefaultRegionNeuron
        {
            get => this.selectedDefaultRegionNeuron;
            set
            {
                if (this.selectedDefaultRegionNeuron != value)
                {
                    this.selectedDefaultRegionNeuron = value;

                    if (this.selectedDefaultRegionNeuron != null)
                    {
                        this.NavigationManager.NavigateTo(
                            Tree.BuildAvatarUrl(this.NavigationManager.Uri, this.AvatarUrl) + "&regionid=" + value.Neuron.Id,
                            true);
                        this.selectedDefaultRegionNeuron = null;
                    }
                }
            }
        }

        private bool IsSearchDefaultRegionNeuronVisible { get; set; } = false;

        private ContextMenuOption selectedOption = ContextMenuOption.New;

        private EventCallback<ContextMenuOption> internalSelectedOptionChanged { get; set; }

        public ContextMenuOption SelectedOption
        {
            get => this.selectedOption;
            set
            {
                if (this.selectedOption != value)
                {
                    this.selectedOption = value;

                    this.internalSelectedOptionChanged.InvokeAsync(this.selectedOption);
                }
            }
        }

        private bool ControlsEnabled { get; set; } = true;

        private TreeNeuronViewModel SelectedNeuron { get; set; } = null;

        private Neuron InitialRegionNeuron { get; set; } = null;

        private IEnumerable<Neuron> InitialPostsynapticNeurons { get; set; } = null;

        private Neuron editNeuron = null;
        private Neuron EditNeuron
        {
            get => this.editNeuron;
            set
            {
                this.editNeuron = value;
            }
        }

        private string ProcessSelectionTag(string format) => this.SelectedNeuron is TreeNeuronViewModel ?
                string.Format(format, this.SelectedNeuron.Neuron.Tag) :
                "[Error: Not a valid Neuron]";

        private async Task Reload()
        {
            await this.ToastService.UITryHandler(
                async () =>
                {
                    await this.SetReloading(true);
                    this.Children.Clear();
                    var ns = await Tree.GetOrderedNeurons(this);
                    var children = ns.Select(nr => new TreeNeuronViewModel(new Neuron(nr), this.AvatarUrl, this.NeuronQueryService));
                    ((List<TreeNeuronViewModel>)this.Children).AddRange(children);
                    this.NewItemsCount = 0;

                    if (this.RenderDirection == RenderDirectionValue.BottomToTop)
                        await this.ScrollToFragment("bottom");

                    QueryUrl.TryParse(this.AvatarUrl, out QueryUrl result);
                    this.serverPushPublicKey = (await this.SubscriptionsQueryService.GetServerConfigurationAsync(result.AvatarUrl)).ServerPublicKey;

                    var objRef = DotNetObjectReference.Create(this);
                    await this.JsRuntime.InvokeVoidAsync("Subscribe", objRef, this.serverPushPublicKey);

                    return false;
                },
                () => "Tree reload",
                postActionInvoker: async () =>
                {
                    await this.SetReloading(false);
                    await this.LoadGraph();
                }
            );
        }

        private async Task SetReloading(bool value)
        {
            this.reloading = value;
            await this.InvokeAsync(() => this.StateHasChanged());
        }

        private async Task ScrollToFragment(string elementId)
        {
            // https://github.com/WICG/scroll-to-text-fragment/
            if (!string.IsNullOrEmpty(elementId))
            {
                await this.JsRuntime.InvokeVoidAsync("BlazorScrollToId", elementId);
            }
        }

        private async void MenuRequested() => this.IsContextMenuVisible = true;

        private async void InfoRequested() => this.IsInfoVisible = true;

        private async void ConfirmDelete()
        {
            if (QueryUrl.TryParse(this.AvatarUrl, out QueryUrl result))
            {
                try
                {
                    string description = string.Empty;

                    if (this.SelectedNeuron.Neuron.Type == RelativeType.NotSet)
                    {
                        await this.NeuronApplicationService.DeactivateNeuron(
                            result.AvatarUrl,
                            this.SelectedNeuron.Neuron.Id,
                            this.SelectedNeuron.Neuron.Version
                            );
                        description = "Neuron removed";
                    }
                    else
                    {
                        await this.TerminalApplicationService.DeactivateTerminal(
                            result.AvatarUrl,
                            this.SelectedNeuron.Neuron.Terminal.Id,
                            this.SelectedNeuron.Neuron.Terminal.Version
                            );
                        description = "Terminal removed";
                    }
                    Helper.ReinitializeOption(o => this.SelectedOption = o);

                    this.ToastService.ShowSuccess($"{description} successfully.");
                }
                catch (Exception ex)
                {
                    this.ToastService.ShowError(ex.Message);
                }
            }
        }

        private async Task LoadGraph()
        {
            var allNodes = new List<Node>();
            Tree.ExtractNodes(this.Children.ToArray(), allNodes);

            var distinctNodes = new List<Node>();
            // get distinct nodes
            allNodes.ForEach(n =>
            {
                if (!distinctNodes.Any(dn => dn.id == n.id))
                    distinctNodes.Add(new Node() { id = n.id, tag = n.tag });
            });

            // get links
            var links = new List<Link>();
            Tree.ExtractLinks(this.Children.ToArray(), distinctNodes, links);

            await this.JsRuntime.InvokeVoidAsync("displayGraph", distinctNodes, links);
        }

        private static void ExtractNodes(IEnumerable<TreeNeuronViewModel> children, List<Node> allNodes)
        {
            allNodes.AddRange(children.Select(c => new Node { id = c.Neuron.Id, tag = c.Neuron.Tag }).ToArray());

            children.ToList().ForEach(c => Tree.ExtractNodes(c.Children, allNodes));
        }

        private static void ExtractLinks(IEnumerable<TreeNeuronViewModel> children, List<Node> distinctNodes, List<Link> links)
        {
            children.ToList().ForEach(c =>
            {
                if (c.Neuron.Type != RelativeType.NotSet)
                {
                    var tsource = distinctNodes.FindIndex(n => n.id == c.Neuron.Terminal.PresynapticNeuronId);
                    var ttarget = distinctNodes.FindIndex(n => n.id == c.Neuron.Terminal.PostsynapticNeuronId);

                    var typeVal = (c.Neuron.Terminal.Strength == "1" ? "full" : "partial") + (c.Neuron.Terminal.Effect == "-1" ? "inhibit" : "excite");
                    if (!links.Any(gl => gl.source == tsource && gl.target == ttarget))
                        links.Add(new Link() { source = tsource, target = ttarget, type = typeVal });
                }
            });

            children.ToList().ForEach(c => Tree.ExtractLinks(c.Children.ToArray(), distinctNodes, links));
        }

        private void CopyAvatarUrl()
        {
            this.JsRuntime.InvokeVoidAsync("copyToClipboard", Tree.BuildAvatarUrl(this.NavigationManager.Uri, this.AvatarUrl));
            this.ToastService.ShowInfo($"Copied successfully.");
            this.optionsDropdown.Hide();
        }

        private static string BuildAvatarUrl(string baseUrl, string avatarUrl)
        {
            var parsedBasedUrl = baseUrl.Substring(0, baseUrl.IndexOf("?") == -1 ? baseUrl.Length : baseUrl.IndexOf("?"));
            var prefixKeyword = "?avatarurl=";
            var encodedUrl = System.Net.WebUtility.UrlEncode(avatarUrl);
            return parsedBasedUrl + prefixKeyword + encodedUrl;
        }

        private async Task SubscribeWithBrowser()
        {
            QueryUrl.TryParse(this.AvatarUrl, out QueryUrl result);

            var subscriptionInfo = new SubscriptionInfo()
            {
                AvatarUrl = this.AvatarUrl
            };

            var receiverInfo = new BrowserReceiverInfo()
            {
                Name = this.deviceName,
                PushAuth = this.pushAuth,
                PushEndpoint = this.pushEndpoint,
                PushP256DH = this.pushP256DH
            };

            await this.SubscriptionApplicationService.SubscribeAsync(result.AvatarUrl, subscriptionInfo, receiverInfo);
        }

        private async Task SubscribeWithEmail()
        {
            string emailDescription = string.Empty;

            await this.ToastService.UITryHandler(
                async () =>
                {
                    QueryUrl.TryParse(this.AvatarUrl, out QueryUrl qu);

                    var subscriptionInfo = new SubscriptionInfo()
                    {
                        AvatarUrl = this.AvatarUrl
                    };

                    var e = this.HttpContextAccessor.HttpContext.User?.Claims.SingleOrDefault(i => i.Type == "email");

                    var result = e != null;

                    if (result)
                    {
                        emailDescription += " using '" + e.Value + "'";
                        var receiverInfo = new SmtpReceiverInfo()
                        {
                            EmailAddress = e.Value
                        };

                        await this.SubscriptionApplicationService.SubscribeAsync(qu.AvatarUrl, subscriptionInfo, receiverInfo);
                    }
                    else
                        throw new InvalidOperationException("User not signed-in.");

                    return result;
                },                
                () => "E-mail Subscription" + emailDescription,
                () => this.optionsDropdown.Hide()
            );
        }

        #region Subscription Interop
        private bool notificationsSupported = false;
        private bool serviceWorkerRegistered = false;
        private string permissionStatus = string.Empty;
        private string deviceName = string.Empty;
        private string pushP256DH;
        private string pushAuth;
        private string pushEndpoint;
        private string serverPushPublicKey;

        [JSInvokable]
        public void SetPermissionStatus(string status)
        {
            permissionStatus = status;
            StateHasChanged();
        }

        [JSInvokable]
        public void SetSupportState(bool isNotificationSupported)
        {
            notificationsSupported = isNotificationSupported;
            StateHasChanged();
        }

        [JSInvokable]
        public void SetServiceWorkerStatus(bool isRegistered)
        {
            serviceWorkerRegistered = isRegistered;
            StateHasChanged();
        }

        [JSInvokable]
        public void SetDeviceProperties(string name, string pushp256dh, string pushAuth, string pushEndpoint)
        {
            this.deviceName = name;
            this.pushP256DH = pushp256dh;
            this.pushAuth = pushAuth;
            this.pushEndpoint = pushEndpoint;
        }
        #endregion

        [Parameter]
        public IHttpContextAccessor HttpContextAccessor { get; set; }
        [Parameter] 
        public INeuronQueryService NeuronQueryService { get; set; }
        [Parameter] 
        public INeuronApplicationService NeuronApplicationService { get; set; }
        [Parameter] 
        public ITerminalApplicationService TerminalApplicationService { get; set; }
        [Parameter] 
        public IToastService ToastService { get; set; }
        [Parameter] 
        public NavigationManager NavigationManager { get; set; }
        [Parameter] 
        public IJSRuntime JsRuntime { get; set; }
        [Parameter] 
        public ISettingsService SettingsService { get; set; }
        [Parameter]
        public IIdentityService IdentityService { get; set; }
        [Parameter]
        public ISubscriptionApplicationService SubscriptionApplicationService { get; set; }
        [Parameter]
        public ISubscriptionQueryService SubscriptionsQueryService { get; set; }
        [Parameter]
        public IPluginSettingsService PluginSettingsService { get => this.pluginSettingsService; set { this.pluginSettingsService = (TreePluginSettingsService) value; } }
    }
}