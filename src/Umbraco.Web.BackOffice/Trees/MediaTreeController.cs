using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Actions;
using Umbraco.Cms.Core.Actions.MediaActions;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.ContentEditing;
using Umbraco.Cms.Core.Models.Entities;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Models.Trees;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Trees;
using Umbraco.Cms.Infrastructure.Search;
using Umbraco.Cms.Web.Common.Attributes;
using Umbraco.Cms.Web.Common.Authorization;
using Umbraco.Extensions;

namespace Umbraco.Cms.Web.BackOffice.Trees;

[Authorize(Policy = AuthorizationPolicies.SectionAccessForMediaTree)]
[Tree(Constants.Applications.Media, Constants.Trees.Media)]
[PluginController(Constants.Web.Mvc.BackOfficeTreeArea)]
[CoreTree]
[SearchableTree("searchResultFormatter", "configureMediaResult", 20)]
public class MediaTreeController : ContentTreeControllerBase, ISearchableTree
{
    private readonly ActionCollection _actions;
    private readonly AppCaches _appCaches;
    private readonly IBackOfficeSecurityAccessor _backofficeSecurityAccessor;
    private readonly IMediaService _mediaService;
    private readonly IEntityService _entityService;
    private readonly ILocalizationService _localizationService;
    private readonly IMenuItemCollectionFactory _menuItemCollectionFactory;
    private readonly UmbracoTreeSearcher _treeSearcher;
    private readonly IUserService _userService;

    private int[]? _userStartNodes;

    public MediaTreeController(
        ILocalizedTextService localizedTextService,
        UmbracoApiControllerTypeCollection umbracoApiControllerTypeCollection,
        IMenuItemCollectionFactory menuItemCollectionFactory,
        IEntityService entityService,
        IBackOfficeSecurityAccessor backofficeSecurityAccessor,
        ILogger<MediaTreeController> logger,
        ActionCollection actionCollection,
        IUserService userService,
        IDataTypeService dataTypeService,
        UmbracoTreeSearcher treeSearcher,
        ActionCollection actions,
        IMediaService mediaService,
        ILocalizationService localizationService,
        IEventAggregator eventAggregator,
        AppCaches appCaches)
        : base(
            localizedTextService,
            umbracoApiControllerTypeCollection,
            menuItemCollectionFactory,
            entityService,
            backofficeSecurityAccessor,
            logger,
            actionCollection,
            userService,
            dataTypeService,
            eventAggregator,
            appCaches)
    {
        _treeSearcher = treeSearcher;
        _actions = actions;
        _menuItemCollectionFactory = menuItemCollectionFactory;
        _backofficeSecurityAccessor = backofficeSecurityAccessor;
        _mediaService = mediaService;
        _entityService = entityService;
        _userService = userService;
        _localizationService = localizationService;
        _appCaches = appCaches;
    }

    protected override int RecycleBinId => Constants.System.RecycleBinMedia;

    protected override bool RecycleBinSmells => _mediaService.RecycleBinSmells();

    protected override int[] UserStartNodes
        => _userStartNodes ??= _backofficeSecurityAccessor.BackOfficeSecurity?.CurrentUser?.CalculateMediaStartNodeIds(_entityService, _appCaches) ?? Array.Empty<int>();

    protected override UmbracoObjectTypes UmbracoObjectType => UmbracoObjectTypes.Media;

    public async Task<EntitySearchResults> SearchAsync(string query, int pageSize, long pageIndex, string? searchFrom = null)
    {
        IEnumerable<SearchResultEntity> results = _treeSearcher.ExamineSearch(query, UmbracoEntityTypes.Media, pageSize, pageIndex, out var totalFound, searchFrom);
        return new EntitySearchResults(results, totalFound);
    }

    /// <summary>
    ///     Creates a tree node for a content item based on an UmbracoEntity
    /// </summary>
    /// <param name="e"></param>
    /// <param name="parentId"></param>
    /// <param name="queryStrings"></param>
    /// <returns></returns>
    protected override TreeNode? GetSingleTreeNode(IEntitySlim entity, string parentId, FormCollection? queryStrings)
    {
        IEnumerable<MenuItem> allowedUserOptions = GetAllowedUserMenuItemsForNode(entity);
        if (CanUserAccessNode(entity, allowedUserOptions, string.Empty))
        {
            TreeNode node = CreateTreeNode(
                entity,
                Constants.ObjectTypes.Media,
                parentId,
                queryStrings,
                entity.HasChildren);

            // entity is either a container, or a media
            if (entity.IsContainer)
            {
                node.SetContainerStyle();
                node.AdditionalData.Add("isContainer", true);
            }
            else
            {
                var contentEntity = (IContentEntitySlim)entity;
                node.AdditionalData.Add("contentType", contentEntity.ContentTypeAlias);
            }

            return node;
        }

        return null;
    }

    protected override ActionResult<MenuItemCollection> PerformGetMenuForNode(string id, FormCollection queryStrings)
    {
        if (id == Constants.System.RootString)
        {
            MenuItemCollection? menu = _menuItemCollectionFactory.Create();

            if (!UserStartNodes.Contains(Constants.System.Root))
            {
                menu.Items.Add(new RefreshNode(LocalizedTextService, true));
                return menu;
            }

            menu.DefaultMenuAlias = ActionMediaNew.ActionAlias;
            EntityPermission permission = _userService
                .GetPermissions(_backofficeSecurityAccessor.BackOfficeSecurity?.CurrentUser, Constants.System.Root)
                .First();

            IEnumerable<MenuItem> nodeActions = _actions.FromEntityPermission(permission)
                .Select(x => new MenuItem(x));

            menu.Items.Add<ActionMediaNew>(LocalizedTextService, opensDialog: true, useLegacyIcon: false);
            menu.Items.Add<ActionMediaSort>(LocalizedTextService, hasSeparator: true, opensDialog: true, useLegacyIcon: false);

            //filter the standard items
            FilterUserAllowedMenuItems(menu, nodeActions);

            if (menu.Items.Any())
            {
                menu.Items.Last().SeparatorBefore = true;
            }

            // add default actions for *all* users
            menu.Items.Add(new RefreshNode(LocalizedTextService, true));

            return menu;
        }

        // return a normal node menu:
        if (!int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out int iid))
        {
            return NotFound();
        }

        IEntitySlim? item = _entityService.Get(iid, UmbracoObjectTypes.Media);
        if (item == null)
        {
            return NotFound();
        }

        // if the user has no path access for this node, all they can do is refresh
        if (!_backofficeSecurityAccessor.BackOfficeSecurity?.CurrentUser?.HasContentPathAccess(item, _entityService, _appCaches) ?? false)
        {
            MenuItemCollection menu = _menuItemCollectionFactory.Create();
            menu.Items.Add(new RefreshNode(LocalizedTextService, true));
            return menu;
        }

        MenuItemCollection nodeMenu = GetAllNodeMenuItems(item);

        // if the content node is in the recycle bin, don't have a default menu, just show the regular menu
        if (item.Path.Split(Constants.CharArrays.Comma, StringSplitOptions.RemoveEmptyEntries)
            .Contains(RecycleBinId.ToInvariantString()))
        {
            nodeMenu.DefaultMenuAlias = null;
            nodeMenu = GetNodeMenuItemsForDeletedMedia(item);
        }
        else
        {
            //set the default to create
            nodeMenu.DefaultMenuAlias = ActionMediaNew.ActionAlias;
        }

        IEnumerable<MenuItem> allowedMenuItems = GetAllowedUserMenuItemsForNode(item);
        FilterUserAllowedMenuItems(nodeMenu, allowedMenuItems);

        return nodeMenu;
    }

    /// <summary>
    /// Returns true or false if the current user has access to the node based on the user's allowed start node (path) access.
    /// </summary>
    protected override bool HasPathAccess(string id, FormCollection queryStrings)
    {
        IEntitySlim? entity = GetEntityFromId(id);

        return HasPathAccess(entity, queryStrings);
    }

    /// <summary>
    /// Returns a collection of all menu items that can be on a media node.
    /// </summary>
    protected MenuItemCollection GetAllNodeMenuItems(IUmbracoEntity item)
    {
        MenuItemCollection menu = _menuItemCollectionFactory.Create();
        AddActionNode<ActionMediaNew>(menu, opensDialog: true, useLegacyIcon: false);
        AddActionNode<ActionMediaDelete>(menu, opensDialog: true, useLegacyIcon: false);
        AddActionNode<ActionMediaMove>(menu, hasSeparator: true, opensDialog: true, useLegacyIcon: false);
        AddActionNode<ActionMediaSort>(menu, hasSeparator: true, opensDialog: true, useLegacyIcon: false);
        AddActionNode<ActionMediaRights>(menu, opensDialog: true, useLegacyIcon: false);

        if (!(item is MediaEntitySlim mediaEntity && mediaEntity.IsContainer))
        {
            menu.Items.Add(new RefreshNode(LocalizedTextService, true));
        }

        return menu;
    }

    /// <summary>
    /// Returns a collection of all menu items that can be on a deleted (in recycle bin) media node.
    /// </summary>
    protected MenuItemCollection GetNodeMenuItemsForDeletedMedia(IUmbracoEntity item)
    {
        MenuItemCollection menu = _menuItemCollectionFactory.Create();
        menu.Items.Add<ActionMediaRestore>(LocalizedTextService, opensDialog: true, useLegacyIcon: false);
        menu.Items.Add<ActionMediaMove>(LocalizedTextService, opensDialog: true, useLegacyIcon: false);
        menu.Items.Add<ActionMediaDelete>(LocalizedTextService, opensDialog: true, useLegacyIcon: false);

        menu.Items.Add(new RefreshNode(LocalizedTextService, true));

        return menu;
    }

    /// <summary>
    /// Returns a collection of all menu items that can be on a media node.
    /// </summary>
    internal IEnumerable<MenuItem> GetAllowedUserMenuItemsForNode(IUmbracoEntity dd)
    {
        IEnumerable<string> permissionsForPath = _userService
            .GetPermissionsForPath(_backofficeSecurityAccessor.BackOfficeSecurity?.CurrentUser, dd.Path)
            .GetAllPermissions();
        return _actions.GetByLetters(permissionsForPath).Select(x => new MenuItem(x));
    }

    private void AddActionNode<TAction>(MenuItemCollection menu, bool hasSeparator = false, bool opensDialog = false, bool useLegacyIcon = true)
    where TAction : IAction
    {
        menu.Items.Add<TAction>(LocalizedTextService, hasSeparator, opensDialog, useLegacyIcon);
    }
}
