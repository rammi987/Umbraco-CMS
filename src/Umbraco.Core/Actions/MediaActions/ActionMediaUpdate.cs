// Copyright (c) Umbraco.
// See LICENSE for more details.

namespace Umbraco.Cms.Core.Actions.MediaActions;

/// <summary>
///     This action is invoked upon updating of a media
/// </summary>
/// <seealso cref="Umbraco.Cms.Core.Actions.IAction" />
public class ActionMediaUpdate : IAction
{
    /// <inheritdoc />
    public char Letter => 'B';

    /// <inheritdoc />
    public bool ShowInNotifier => true;

    /// <inheritdoc />
    public bool CanBePermissionAssigned => true;

    /// <inheritdoc />
    public string Icon => "icon-save";

    /// <inheritdoc />
    public string Alias => "update";

    /// <inheritdoc />
    public string Category => "media";
}
