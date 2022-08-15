// Copyright (c) Umbraco.
// See LICENSE for more details.


namespace Umbraco.Cms.Core.Actions.MediaActions;

/// <summary>
/// This action is invoked when a media is deleted
/// </summary>
public class ActionMediaDelete : IAction
{
    /// <inheritdoc/>
    public char Letter => 'G';

    /// <inheritdoc/>
    public bool ShowInNotifier => true;

    /// <inheritdoc/>
    public bool CanBePermissionAssigned => true;

    /// <inheritdoc/>
    public string Icon => "icon-delete";

    /// <inheritdoc/>
    public string Alias => "mediaDelete";

    /// <inheritdoc/>
    public string Category => "media";
}
