// Copyright (c) Umbraco.
// See LICENSE for more details.


namespace Umbraco.Cms.Core.Actions.MediaActions;

/// <summary>
///     This action is invoked upon creation of a media
/// </summary>
public class ActionMediaMove : IAction
{
    /// <inheritdoc />
    public char Letter => 'Y';

    /// <inheritdoc />
    public bool ShowInNotifier => true;

    /// <inheritdoc />
    public bool CanBePermissionAssigned => true;

    /// <inheritdoc />
    public string Icon => "icon-enter";

    /// <inheritdoc />
    public string Alias => "move";

    /// <inheritdoc />
    public string? Category => "media";
}
