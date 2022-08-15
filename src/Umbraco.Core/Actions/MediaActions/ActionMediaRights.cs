// Copyright (c) Umbraco.
// See LICENSE for more details.

namespace Umbraco.Cms.Core.Actions.MediaActions;

/// <summary>
///     This action is invoked when rights are changed on a media
/// </summary>
public class ActionMediaRights : IAction
{
    /// <inheritdoc />
    public char Letter => 'L';

    /// <inheritdoc />
    public bool ShowInNotifier => true;

    /// <inheritdoc />
    public bool CanBePermissionAssigned => true;

    /// <inheritdoc />
    public string Icon => "icon-vcard";

    /// <inheritdoc />
    public string Alias => "rightsMedia";

    /// <inheritdoc />
    public string? Category => "media";
}
