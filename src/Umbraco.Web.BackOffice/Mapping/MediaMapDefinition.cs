using System.Globalization;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Core.Dictionary;
using Umbraco.Cms.Core.Mapping;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.ContentEditing;
using Umbraco.Cms.Core.Models.Mapping;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Web.BackOffice.Trees;
using Umbraco.Extensions;

namespace Umbraco.Cms.Web.BackOffice.Mapping;

/// <summary>
///     Declares model mappings for media.
/// </summary>
public class MediaMapDefinition : IMapDefinition
{
    private readonly CommonMapper _commonMapper;
    private readonly CommonTreeNodeMapper _commonTreeNodeMapper;
    private readonly ContentSettings _contentSettings;
    private readonly IMediaService _mediaService;
    private readonly IMediaTypeService _mediaTypeService;
    private readonly MediaUrlGeneratorCollection _mediaUrlGenerators;
    private readonly TabsAndPropertiesMapper<IMedia> _tabsAndPropertiesMapper;
    private readonly AppCaches _appCaches;
    private readonly IEntityService _entityService;

    public MediaMapDefinition(ICultureDictionary cultureDictionary, CommonMapper commonMapper,
        CommonTreeNodeMapper commonTreeNodeMapper, IMediaService mediaService, IMediaTypeService mediaTypeService,
        ILocalizedTextService localizedTextService, MediaUrlGeneratorCollection mediaUrlGenerators,
        IOptions<ContentSettings> contentSettings, IContentTypeBaseServiceProvider contentTypeBaseServiceProvider,
        IEntityService entityService,
        AppCaches appCaches)
    {
        _commonMapper = commonMapper;
        _commonTreeNodeMapper = commonTreeNodeMapper;
        _mediaService = mediaService;
        _mediaTypeService = mediaTypeService;
        _mediaUrlGenerators = mediaUrlGenerators;
        _contentSettings = contentSettings.Value ?? throw new ArgumentNullException(nameof(contentSettings));
        _appCaches = appCaches;
        _entityService = entityService;

        _tabsAndPropertiesMapper =
            new TabsAndPropertiesMapper<IMedia>(cultureDictionary, localizedTextService,
                contentTypeBaseServiceProvider);
    }

    public void DefineMaps(IUmbracoMapper mapper)
    {
        mapper.Define<IMedia, ContentPropertyCollectionDto>((source, context) => new ContentPropertyCollectionDto(),
            Map);
        mapper.Define<IMedia, MediaItemDisplay>((source, context) => new MediaItemDisplay(), Map);
        mapper.Define<IMedia, ContentItemBasic<ContentPropertyBasic>>(
            (source, context) => new ContentItemBasic<ContentPropertyBasic>(), Map);
    }

    // Umbraco.Code.MapAll
    private static void Map(IMedia source, ContentPropertyCollectionDto target, MapperContext context) =>
        target.Properties = context.MapEnumerable<IProperty, ContentPropertyDto>(source.Properties).WhereNotNull();

    // Umbraco.Code.MapAll -Properties -Errors -Edited -Updater -Alias -IsContainer
    private void Map(IMedia source, MediaItemDisplay target, MapperContext context)
    {
        // Both GetActions and DetermineIsChildOfListView use parent, so get it once here
        // Parent might already be in context, so check there before using content service
        IMedia? parent;
        if (context.Items.TryGetValue("Parent", out var parentObj) &&
            parentObj is IMedia typedParent)
        {
            parent = typedParent;
        }
        else
        {
            parent = _mediaService.GetParent(source);
        }

        //target.AllowedActions = _commonMapper.GetActions(source, parent, context);
        target.ContentApps = _commonMapper.GetContentAppsForEntity(source);
        target.ContentType = _commonMapper.GetContentType(source, context);
        target.ContentTypeId = source.ContentType.Id;
        target.ContentTypeAlias = source.ContentType.Alias;
        target.ContentTypeName = source.ContentType.Name;
        target.CreateDate = source.CreateDate;
        target.Icon = source.ContentType.Icon;
        target.Id = source.Id;
        target.IsChildOfListView = DetermineIsChildOfListView(source, parent, context);
        target.Key = source.Key;
        target.MediaLink = string.Join(",", source.GetUrls(_contentSettings, _mediaUrlGenerators));
        target.Name = source.Name;
        target.Owner = _commonMapper.GetOwner(source, context);
        target.ParentId = source.ParentId;
        target.Path = source.Path;
        target.SortOrder = source.SortOrder;
        target.State = null;
        target.Tabs = _tabsAndPropertiesMapper.Map(source, context);
        target.Trashed = source.Trashed;
        target.TreeNodeUrl = _commonTreeNodeMapper.GetTreeNodeUrl<MediaTreeController>(source);
        target.Udi = Udi.Create(Constants.UdiEntityType.Media, source.Key);
        target.UpdateDate = source.UpdateDate;
        target.VariesByCulture = source.ContentType.VariesByCulture();
    }

    // Umbraco.Code.MapAll -Edited -Updater -Alias
    private void Map(IMedia source, ContentItemBasic<ContentPropertyBasic> target, MapperContext context)
    {
        target.ContentTypeId = source.ContentType.Id;
        target.ContentTypeAlias = source.ContentType.Alias;
        target.CreateDate = source.CreateDate;
        target.Icon = source.ContentType.Icon;
        target.Id = source.Id;
        target.Key = source.Key;
        target.Name = source.Name;
        target.Owner = _commonMapper.GetOwner(source, context);
        target.ParentId = source.ParentId;
        target.Path = source.Path;
        target.Properties = context.MapEnumerable<IProperty, ContentPropertyBasic>(source.Properties).WhereNotNull();
        target.SortOrder = source.SortOrder;
        target.State = null;
        target.Trashed = source.Trashed;
        target.Udi = Udi.Create(Constants.UdiEntityType.Media, source.Key);
        target.UpdateDate = source.UpdateDate;
        target.VariesByCulture = source.ContentType.VariesByCulture();
    }

    /// <summary>
    ///     Checks if the content item is a descendant of a list view
    /// </summary>
    /// <param name="source"></param>
    /// <param name="parent"></param>
    /// <param name="context"></param>
    /// <returns>
    ///     Returns true if the content item is a descendant of a list view and where the content is
    ///     not a current user's start node.
    /// </returns>
    /// <remarks>
    ///     We must check if it's the current user's start node because in that case we will actually be
    ///     rendering the tree node underneath the list view to visually show context. In this case we return
    ///     false because the item is technically not being rendered as part of a list view but instead as a
    ///     real tree node. If we didn't perform this check then tree syncing wouldn't work correctly.
    /// </remarks>
    private bool DetermineIsChildOfListView(IMedia source, IMedia? parent, MapperContext context)
    {
        var userStartNodes = Array.Empty<int>();

        // In cases where a user's start node is below a list view, we will actually render
        // out the tree to that start node and in that case for that start node, we want to return
        // false here.
        if (context.HasItems && context.Items.TryGetValue("CurrentUser", out var usr) && usr is IUser currentUser)
        {
            userStartNodes = currentUser.CalculateContentStartNodeIds(_entityService, _appCaches);
            if (!userStartNodes?.Contains(Constants.System.Root) ?? false)
            {
                // return false if this is the user's actual start node, the node will be rendered in the tree
                // regardless of if it's a list view or not
                if (userStartNodes?.Contains(source.Id) ?? false)
                {
                    return false;
                }
            }
        }

        if (parent == null)
        {
            return false;
        }

        var pathParts = parent.Path.Split(Constants.CharArrays.Comma).Select(x =>
            int.TryParse(x, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : 0).ToList();

        if (userStartNodes is not null)
        {
            // reduce the path parts so we exclude top level content items that
            // are higher up than a user's start nodes
            foreach (var n in userStartNodes)
            {
                var index = pathParts.IndexOf(n);
                if (index != -1)
                {
                    // now trim all top level start nodes to the found index
                    for (var i = 0; i < index; i++)
                    {
                        pathParts.RemoveAt(0);
                    }
                }
            }
        }

        return parent.ContentType.IsContainer || _mediaTypeService.HasContainerInPath(pathParts.ToArray());
    }
}
