// Copyright (c) Umbraco.
// See LICENSE for more details.


namespace Umbraco.Cms.Core.Actions.MediaActions;

/// <summary>
///     This action is invoked when children to a media is being sorted
/// </summary>
public class ActionMediaSort : IAction
{
    /// <inheritdoc />
    public char Letter => 'J';

    /// <inheritdoc />
    public bool ShowInNotifier => true;

    /// <inheritdoc />
    public bool CanBePermissionAssigned => true;

    /// <inheritdoc />
    public string Icon => "icon-navigation-vertical";

    /// <inheritdoc />
    public string Alias => "sortMedia";

    /// <inheritdoc />
    public string? Category => "media";
}
