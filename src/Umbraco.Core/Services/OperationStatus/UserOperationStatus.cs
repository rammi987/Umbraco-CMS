﻿namespace Umbraco.Cms.Core.Services.OperationStatus;

/// <summary>
/// Used to signal a user operation succeeded or an atomic failure reason
/// </summary>
public enum UserOperationStatus
{
    Success,
    MissingUser,
    MissingUserGroup,
    UserNameIsNotEmail,
    EmailCannotBeChanged,
    NoUserGroup,
    DuplicateUserName,
    InvalidEmail,
    DuplicateEmail,
    Unauthorized,
    Forbidden,
    CancelledByNotification,
    UserNotFound,
    AvatarFileNotFound,
    CannotInvite,
    CannotDelete,
    CannotDisableSelf,
    CannotDeleteSelf,
    CannotDisableInvitedUser,
    OldPasswordRequired,
    InvalidAvatar,
    InvalidIsoCode,
    InvalidInviteToken,
    InvalidPasswordResetToken,
    ContentStartNodeNotFound,
    MediaStartNodeNotFound,
    ContentNodeNotFound,
    MediaNodeNotFound,
    UnknownFailure,
    CannotPasswordReset,
    NotInInviteState
}