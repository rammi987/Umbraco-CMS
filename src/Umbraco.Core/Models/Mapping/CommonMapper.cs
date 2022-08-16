using Umbraco.Cms.Core.ContentApps;
using Umbraco.Cms.Core.Mapping;
using Umbraco.Cms.Core.Models.ContentEditing;
using Umbraco.Cms.Core.Models.Entities;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Core.Services;
using Umbraco.Extensions;
using UserProfile = Umbraco.Cms.Core.Models.ContentEditing.UserProfile;

namespace Umbraco.Cms.Core.Models.Mapping;

public class CommonMapper
{
    private readonly ContentAppFactoryCollection _contentAppDefinitions;
    private readonly IContentTypeBaseServiceProvider _contentTypeBaseServiceProvider;
    private readonly ILocalizedTextService _localizedTextService;
    private readonly IUserService _userService;
    private readonly IBackOfficeSecurityAccessor _backOfficeSecurityAccessor;

    public CommonMapper(
        IUserService userService,
        IContentTypeBaseServiceProvider contentTypeBaseServiceProvider,
        ContentAppFactoryCollection contentAppDefinitions,
        ILocalizedTextService localizedTextService,
        IBackOfficeSecurityAccessor backOfficeSecurityAccessor)
    {
        _userService = userService;
        _contentTypeBaseServiceProvider = contentTypeBaseServiceProvider;
        _contentAppDefinitions = contentAppDefinitions;
        _localizedTextService = localizedTextService;
        _backOfficeSecurityAccessor = backOfficeSecurityAccessor;
    }

    public UserProfile? GetOwner(IContentBase source, MapperContext context)
    {
        IProfile? profile = source.GetCreatorProfile(_userService);
        return profile == null ? null : context.Map<IProfile, UserProfile>(profile);
    }

    public UserProfile? GetCreator(IContent source, MapperContext context)
    {
        IProfile? profile = source.GetWriterProfile(_userService);
        return profile == null ? null : context.Map<IProfile, UserProfile>(profile);
    }

    public ContentTypeBasic? GetContentType(IContentBase source, MapperContext context)
    {
        IContentTypeComposition? contentType = _contentTypeBaseServiceProvider.GetContentTypeOf(source);
        ContentTypeBasic? contentTypeBasic = context.Map<IContentTypeComposition, ContentTypeBasic>(contentType);
        return contentTypeBasic;
    }

    public IEnumerable<ContentApp> GetContentApps(IUmbracoEntity source) => GetContentAppsForEntity(source);

    public IEnumerable<ContentApp> GetContentAppsForEntity(IEntity source)
    {
        ContentApp[] apps = _contentAppDefinitions.GetContentAppsFor(source).ToArray();

        // localize content app names
        foreach (ContentApp app in apps)
        {
            var localizedAppName = _localizedTextService.Localize("apps", app.Alias);
            if (localizedAppName.Equals($"[{app.Alias}]", StringComparison.OrdinalIgnoreCase) == false)
            {
                app.Name = localizedAppName;
            }
        }

        return apps;
    }

    public IEnumerable<string> GetActions(ITreeEntity source, ITreeEntity? parent, MapperContext context)
    {
        IBackOfficeSecurity? backOfficeSecurity = _backOfficeSecurityAccessor.BackOfficeSecurity;

        //cannot check permissions without a context
        if (backOfficeSecurity is null)
        {
            return Enumerable.Empty<string>();
        }

        string path;
        if (source.HasIdentity)
        {
            path = source.Path;
        }
        else
        {
            path = parent == null ? "-1" : parent.Path;
        }

        // A bit of a mess, but we need to ensure that all the required values are here AND that they're the right type.
        if (context.Items.TryGetValue("CurrentUser", out var userObject) &&
            context.Items.TryGetValue("Permissions", out var permissionsObject) &&
            userObject is IUser currentUser &&
            permissionsObject is Dictionary<string, EntityPermissionSet> permissionsDict)
        {
            // If we already have permissions for a given path,
            // and the current user is the same as was used to generate the permissions, return the stored permissions.
            if (backOfficeSecurity.CurrentUser?.Id == currentUser.Id &&
                permissionsDict.TryGetValue(path, out EntityPermissionSet? permissions))
            {
                return permissions.GetAllPermissions();
            }
        }

        // TODO: This is certainly not ideal usage here - perhaps the best way to deal with this in the future is
        // with the IUmbracoContextAccessor. In the meantime, if used outside of a web app this will throw a null
        // reference exception :(

        return _userService.GetPermissionsForPath(backOfficeSecurity.CurrentUser, path).GetAllPermissions();
    }
}
