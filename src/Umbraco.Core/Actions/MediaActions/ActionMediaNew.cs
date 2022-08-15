// Copyright (c) Umbraco.
// See LICENSE for more details.

namespace Umbraco.Cms.Core.Actions.MediaActions;

/// <summary>
///     This action is invoked upon creation of a media
/// </summary>
/// <seealso cref="Umbraco.Cms.Core.Actions.IAction" />
public class ActionMediaNew : IAction
{
    /// <inheritdoc />
    public char Letter => 'X';

    /// <inheritdoc />
    public string Alias => "createMedia";

    /// <inheritdoc />
    public string Icon => "icon-add";

    /// <inheritdoc />
    public bool ShowInNotifier => true;

    /// <inheritdoc />
    public bool CanBePermissionAssigned => true;

    /// <inheritdoc />
    public string Category => "media";
}
