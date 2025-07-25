using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Persistence;
using Umbraco.Cms.Core.Persistence.Querying;
using Umbraco.Cms.Core.Persistence.Repositories;
using Umbraco.Cms.Core.Scoping;
using Umbraco.Extensions;

namespace Umbraco.Cms.Core.Services
{
    /// <summary>
    /// Represents the MemberService.
    /// </summary>
    public class MemberService : RepositoryService, IMemberService
    {
        private readonly IMemberRepository _memberRepository;
        private readonly IMemberTypeRepository _memberTypeRepository;
        private readonly IMemberGroupRepository _memberGroupRepository;
        private readonly IAuditRepository _auditRepository;
        private readonly IMemberGroupService _memberGroupService;
        private readonly Lazy<IIdKeyMap> _idKeyMap;

        #region Constructor

        public MemberService(
            ICoreScopeProvider provider,
            ILoggerFactory loggerFactory,
            IEventMessagesFactory eventMessagesFactory,
            IMemberGroupService memberGroupService,
            IMemberRepository memberRepository,
            IMemberTypeRepository memberTypeRepository,
            IMemberGroupRepository memberGroupRepository,
            IAuditRepository auditRepository,
            Lazy<IIdKeyMap> idKeyMap)
            : base(provider, loggerFactory, eventMessagesFactory)
        {
            _memberRepository = memberRepository;
            _memberTypeRepository = memberTypeRepository;
            _memberGroupRepository = memberGroupRepository;
            _auditRepository = auditRepository;
            _idKeyMap = idKeyMap;
            _memberGroupService = memberGroupService ?? throw new ArgumentNullException(nameof(memberGroupService));
        }

        #endregion

        #region Count

        /// <summary>
        /// Gets the total number of Members based on the count type
        /// </summary>
        /// <remarks>
        /// The way the Online count is done is the same way that it is done in the MS SqlMembershipProvider - We query for any members
        /// that have their last active date within the Membership.UserIsOnlineTimeWindow (which is in minutes). It isn't exact science
        /// but that is how MS have made theirs so we'll follow that principal.
        /// </remarks>
        /// <param name="countType"><see cref="MemberCountType"/> to count by</param>
        /// <returns><see cref="int"/> with number of Members for passed in type</returns>
        public int GetCount(MemberCountType countType)
        {
            using ICoreScope scope = ScopeProvider.CreateCoreScope(autoComplete: true);
            scope.ReadLock(Constants.Locks.MemberTree);

            IQuery<IMember>? query;

            switch (countType)
            {
                case MemberCountType.All:
                    query = Query<IMember>();
                    break;
                case MemberCountType.LockedOut:
                    query = Query<IMember>()?.Where(x => x.IsLockedOut == true);
                    break;
                case MemberCountType.Approved:
                    query = Query<IMember>()?.Where(x => x.IsApproved == true);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(countType));
            }

            return _memberRepository.GetCountByQuery(query);
        }

        /// <summary>
        /// Gets the count of Members by an optional MemberType alias
        /// </summary>
        /// <remarks>If no alias is supplied then the count for all Member will be returned</remarks>
        /// <param name="memberTypeAlias">Optional alias for the MemberType when counting number of Members</param>
        /// <returns><see cref="int"/> with number of Members</returns>
        public int Count(string? memberTypeAlias = null)
        {
            using ICoreScope scope = ScopeProvider.CreateCoreScope(autoComplete: true);
            scope.ReadLock(Constants.Locks.MemberTree);
            return _memberRepository.Count(memberTypeAlias);
        }

        #endregion

        #region Create

        public async Task<PagedModel<IMember>> FilterAsync(
            MemberFilter memberFilter,
            string orderBy = "username",
            Direction orderDirection = Direction.Ascending,
            int skip = 0,
            int take = 100)
        {
            using ICoreScope scope = ScopeProvider.CreateCoreScope(autoComplete: true);
            scope.ReadLock(Constants.Locks.MemberTypes);
            scope.ReadLock(Constants.Locks.MemberTree);

            return await _memberRepository.GetPagedByFilterAsync(memberFilter, skip, take, Ordering.By(orderBy, orderDirection));
        }

        /// <summary>
        /// Creates an <see cref="IMember"/> object without persisting it
        /// </summary>
        /// <remarks>This method is convenient for when you need to add properties to a new Member
        /// before persisting it in order to limit the amount of times its saved.
        /// Also note that the returned <see cref="IMember"/> will not have an Id until its saved.</remarks>
        /// <param name="username">Username of the Member to create</param>
        /// <param name="email">Email of the Member to create</param>
        /// <param name="name">Name of the Member to create</param>
        /// <param name="memberTypeAlias">Alias of the MemberType the Member should be based on</param>
        /// <exception cref="ArgumentException">Thrown when a member type for the given alias isn't found</exception>
        /// <returns><see cref="IMember"/></returns>
        public IMember CreateMember(string username, string email, string name, string memberTypeAlias)
        {
            IMemberType memberType = GetMemberType(memberTypeAlias);
            if (memberType == null)
            {
                throw new ArgumentException("No member type with that alias.", nameof(memberTypeAlias));
            }

            var member = new Member(name, email.ToLower().Trim(), username, memberType, 0);

            return member;
        }

        /// <summary>
        /// Creates an <see cref="IMember"/> object without persisting it
        /// </summary>
        /// <remarks>This method is convenient for when you need to add properties to a new Member
        /// before persisting it in order to limit the amount of times its saved.
        /// Also note that the returned <see cref="IMember"/> will not have an Id until its saved.</remarks>
        /// <param name="username">Username of the Member to create</param>
        /// <param name="email">Email of the Member to create</param>
        /// <param name="name">Name of the Member to create</param>
        /// <param name="memberType">MemberType the Member should be based on</param>
        /// <returns><see cref="IMember"/></returns>
        public IMember CreateMember(string username, string email, string name, IMemberType memberType)
        {
            if (memberType == null)
            {
                throw new ArgumentNullException(nameof(memberType));
            }

            var member = new Member(name, email.ToLower().Trim(), username, memberType, 0);

            return member;
        }

        /// <summary>
        /// Creates and persists a new <see cref="IMember"/>
        /// </summary>
        /// <remarks>An <see cref="IMembershipUser"/> can be of type <see cref="IMember"/> or <see cref="IUser"/></remarks>
        /// <param name="username">Username of the <see cref="IMembershipUser"/> to create</param>
        /// <param name="email">Email of the <see cref="IMembershipUser"/> to create</param>
        /// <param name="passwordValue">This value should be the encoded/encrypted/hashed value for the password that will be stored in the database</param>
        /// <param name="memberTypeAlias">Alias of the Type</param>
        /// <returns><see cref="IMember"/></returns>
        IMember IMembershipMemberService<IMember>.CreateWithIdentity(string username, string email, string passwordValue, string memberTypeAlias)
            => CreateMemberWithIdentity(username, email, username, passwordValue, memberTypeAlias);

        /// <summary>
        /// Creates and persists a new <see cref="IMember"/>
        /// </summary>
        /// <remarks>An <see cref="IMembershipUser"/> can be of type <see cref="IMember"/> or <see cref="IUser"/></remarks>
        /// <param name="username">Username of the <see cref="IMembershipUser"/> to create</param>
        /// <param name="email">Email of the <see cref="IMembershipUser"/> to create</param>
        /// <param name="passwordValue">This value should be the encoded/encrypted/hashed value for the password that will be stored in the database</param>
        /// <param name="memberTypeAlias">Alias of the Type</param>
        /// <param name="isApproved"></param>
        /// <returns><see cref="IMember"/></returns>
        IMember IMembershipMemberService<IMember>.CreateWithIdentity(string username, string email, string passwordValue, string memberTypeAlias, bool isApproved)
            => CreateMemberWithIdentity(username, email, username, passwordValue, memberTypeAlias, isApproved);

        public IMember CreateMemberWithIdentity(string username, string email, string memberTypeAlias)
            => CreateMemberWithIdentity(username, email, username, string.Empty, memberTypeAlias);

        public IMember CreateMemberWithIdentity(string username, string email, string memberTypeAlias, bool isApproved)
            => CreateMemberWithIdentity(username, email, username, string.Empty, memberTypeAlias, isApproved);

        public IMember CreateMemberWithIdentity(string username, string email, string name, string memberTypeAlias)
            => CreateMemberWithIdentity(username, email, name, string.Empty, memberTypeAlias);

        public IMember CreateMemberWithIdentity(string username, string email, string name, string memberTypeAlias, bool isApproved)
            => CreateMemberWithIdentity(username, email, name, string.Empty, memberTypeAlias, isApproved);

        /// <summary>
        /// Creates and persists a Member.
        /// </summary>
        /// <remarks>Using this method will persist the Member object before its returned
        /// meaning that it will have an Id available (unlike the <see cref="CreateMember(string, string, string, string)"/> method).</remarks>
        /// <param name="username">Username of the Member to create.</param>
        /// <param name="email">Email of the Member to create.</param>
        /// <param name="name">Name of the Member to create.</param>
        /// <param name="passwordValue">Password value of the Member to create.</param>
        /// <param name="memberTypeAlias">Alias of the MemberType the Member should be based on.</param>
        /// <param name="isApproved">Optional IsApproved of the Member to create.</param>
        /// <returns><see cref="IMember"/></returns>
        public IMember CreateMemberWithIdentity(string username, string email, string name, string passwordValue, string memberTypeAlias, bool isApproved = true)
        {
            using (ICoreScope scope = ScopeProvider.CreateCoreScope())
            {
                // locking the member tree secures member types too
                scope.WriteLock(Constants.Locks.MemberTree);

                IMemberType memberType = GetMemberType(scope, memberTypeAlias); // + locks // + locks
                if (memberType == null)
                {
                    throw new ArgumentException("No member type with that alias.", nameof(memberTypeAlias)); // causes rollback // causes rollback
                }

                var member = new Member(name, email.ToLower().Trim(), username, passwordValue, memberType, isApproved, -1);

                Save(member);

                scope.Complete();

                return member;
            }
        }

        public IMember CreateMemberWithIdentity(string username, string email, IMemberType memberType)
            => CreateMemberWithIdentity(username, email, username, string.Empty, memberType);

        /// <summary>
        /// Creates and persists a Member
        /// </summary>
        /// <remarks>Using this method will persist the Member object before its returned
        /// meaning that it will have an Id available (unlike the CreateMember method)</remarks>
        /// <param name="username">Username of the Member to create</param>
        /// <param name="email">Email of the Member to create</param>
        /// <param name="memberType">MemberType the Member should be based on</param>
        /// <param name="isApproved">Is the member approved.</param>
        /// <returns><see cref="IMember"/></returns>
        public IMember CreateMemberWithIdentity(string username, string email, IMemberType memberType, bool isApproved)
            => CreateMemberWithIdentity(username, email, username, string.Empty, memberType, isApproved);

        public IMember CreateMemberWithIdentity(string username, string email, string name, IMemberType memberType)
            => CreateMemberWithIdentity(username, email, name, string.Empty, memberType);

        /// <summary>
        /// Creates and persists a Member
        /// </summary>
        /// <remarks>Using this method will persist the Member object before its returned
        /// meaning that it will have an Id available (unlike the CreateMember method)</remarks>
        /// <param name="username">Username of the Member to create</param>
        /// <param name="email">Email of the Member to create</param>
        /// <param name="name">Name of the Member to create</param>
        /// <param name="memberType">MemberType the Member should be based on</param>
        /// <param name="isApproved">Is the member approved</param>
        /// <returns><see cref="IMember"/></returns>
        public IMember CreateMemberWithIdentity(string username, string email, string name, IMemberType memberType, bool isApproved)
            => CreateMemberWithIdentity(username, email, name, string.Empty, memberType, isApproved);

        /// <summary>
        /// Creates and persists a Member
        /// </summary>
        /// <remarks>Using this method will persist the Member object before its returned
        /// meaning that it will have an Id available (unlike the CreateMember method)</remarks>
        /// <param name="username">Username of the Member to create</param>
        /// <param name="email">Email of the Member to create</param>
        /// <param name="name">Name of the Member to create</param>
        /// <param name="passwordValue">This value should be the encoded/encrypted/hashed value for the password that will be stored in the database</param>
        /// <param name="memberType">MemberType the Member should be based on</param>
        /// <param name="isApproved">Is the member approved</param>
        /// <returns><see cref="IMember"/></returns>
        private IMember CreateMemberWithIdentity(string username, string email, string name, string passwordValue, IMemberType memberType, bool isApproved = true)
        {
            if (memberType == null)
            {
                throw new ArgumentNullException(nameof(memberType));
            }

            using ICoreScope scope = ScopeProvider.CreateCoreScope();
            scope.WriteLock(Constants.Locks.MemberTree);

            // ensure it all still make sense
            // ensure it all still make sense
            IMemberType? vrfy = GetMemberType(scope, memberType.Alias); // + locks

            if (vrfy == null || vrfy.Id != memberType.Id)
            {
                throw new ArgumentException($"Member type with alias {memberType.Alias} does not exist or is a different member type."); // causes rollback
            }

            var member = new Member(name, email.ToLower().Trim(), username, passwordValue, memberType, isApproved, -1);

            Save(member);

            scope.Complete();

            return member;
        }

        #endregion

        #region Get, Has, Is, Exists...

        /// <summary>
        /// Gets a Member by its integer id
        /// </summary>
        /// <param name="id"><see cref="int"/> Id</param>
        /// <returns><see cref="IMember"/></returns>
        public IMember? GetById(int id)
        {
            using ICoreScope scope = ScopeProvider.CreateCoreScope(autoComplete: true);
            scope.ReadLock(Constants.Locks.MemberTree);
            return _memberRepository.Get(id);
        }

        /// <summary>
        /// Gets a Member by the unique key
        /// </summary>
        /// <remarks>The guid key corresponds to the unique id in the database
        /// and the user id in the membership provider.</remarks>
        /// <param name="id"><see cref="Guid"/> Id</param>
        /// <returns><see cref="IMember"/></returns>
        public IMember? GetById(Guid id)
        {
            using ICoreScope scope = ScopeProvider.CreateCoreScope(autoComplete: true);
            scope.ReadLock(Constants.Locks.MemberTree);
            return GetMemberFromRepository(id);
        }

        /// <summary>
        /// Gets a list of paged <see cref="IMember"/> objects
        /// </summary>
        /// <param name="pageIndex">Current page index</param>
        /// <param name="pageSize">Size of the page</param>
        /// <param name="totalRecords">Total number of records found (out)</param>
        /// <returns><see cref="IEnumerable{IMember}"/></returns>
        public IEnumerable<IMember> GetAll(long pageIndex, int pageSize, out long totalRecords)
        {
            using ICoreScope scope = ScopeProvider.CreateCoreScope(autoComplete: true);
            scope.ReadLock(Constants.Locks.MemberTree);
            return _memberRepository.GetPage(null, pageIndex, pageSize, out totalRecords, null, Ordering.By("LoginName"));
        }

        public IEnumerable<IMember> GetAll(
            long pageIndex,
            int pageSize,
            out long totalRecords,
            string orderBy,
            Direction orderDirection,
            string? memberTypeAlias = null,
            string filter = "") =>
            GetAll(pageIndex, pageSize, out totalRecords, orderBy, orderDirection, true, memberTypeAlias, filter);

        public IEnumerable<IMember> GetAll(
            long pageIndex,
            int pageSize,
            out long totalRecords,
            string orderBy,
            Direction orderDirection,
            bool orderBySystemField,
            string? memberTypeAlias,
            string filter)
        {
            using ICoreScope scope = ScopeProvider.CreateCoreScope(autoComplete: true);
            scope.ReadLock(Constants.Locks.MemberTree);
            IQuery<IMember>? query1 = memberTypeAlias == null ? null : Query<IMember>()?.Where(x => x.ContentTypeAlias == memberTypeAlias);
            int.TryParse(filter, out int filterAsIntId);//considering id,key & name as filter param
            Guid.TryParse(filter, out Guid filterAsGuid);
            IQuery<IMember>? query2 = filter == null ? null : Query<IMember>()?.Where(x => (x.Name != null && x.Name.Contains(filter)) || x.Username.Contains(filter) || x.Email.Contains(filter) || x.Id == filterAsIntId || x.Key ==  filterAsGuid );
            return _memberRepository.GetPage(query1, pageIndex, pageSize, out totalRecords, query2, Ordering.By(orderBy, orderDirection, isCustomField: !orderBySystemField));
        }

        /// <summary>
        /// Gets an <see cref="IMember"/> by its provider key
        /// </summary>
        /// <param name="id">Id to use for retrieval</param>
        /// <returns><see cref="IMember"/></returns>
        public IMember? GetByProviderKey(object id)
        {
            Attempt<Guid> asGuid = id.TryConvertTo<Guid>();
            if (asGuid.Success)
            {
                return GetById(asGuid.Result);
            }

            Attempt<int> asInt = id.TryConvertTo<int>();
            if (asInt.Success)
            {
                return GetById(asInt.Result);
            }

            return null;
        }

        /// <summary>
        /// Get an <see cref="IMember"/> by email. If RequireUniqueEmailForMembers is set to false, then the first member found with the specified email will be returned.
        /// </summary>
        /// <param name="email">Email to use for retrieval</param>
        /// <returns><see cref="IMember"/></returns>
        public IMember? GetByEmail(string email) => GetMembersByEmail(email).FirstOrDefault();

        /// <summary>
        /// Get an list of <see cref="IMember"/> for all members with the specified email.
        /// </summary>
        /// <param name="email">Email to use for retrieval</param>
        /// <returns><see cref="IEnumerable{IMember}"/></returns>
        public IEnumerable<IMember> GetMembersByEmail(string email)
        {
            using ICoreScope scope = ScopeProvider.CreateCoreScope(autoComplete: true);
            scope.ReadLock(Constants.Locks.MemberTree);
            IQuery<IMember> query = Query<IMember>().Where(x => x.Email.Equals(email));
            return _memberRepository.Get(query);
        }

        /// <summary>
        /// Get an <see cref="IMember"/> by username
        /// </summary>
        /// <param name="username">Username to use for retrieval</param>
        /// <returns><see cref="IMember"/></returns>
        public IMember? GetByUsername(string? username)
        {
            using ICoreScope scope = ScopeProvider.CreateCoreScope(autoComplete: true);
            scope.ReadLock(Constants.Locks.MemberTree);
            return _memberRepository.GetByUsername(username);
        }

        /// <summary>
        /// Gets all Members for the specified MemberType alias
        /// </summary>
        /// <param name="memberTypeAlias">Alias of the MemberType</param>
        /// <returns><see cref="IEnumerable{IMember}"/></returns>
        public IEnumerable<IMember> GetMembersByMemberType(string memberTypeAlias)
        {
            using ICoreScope scope = ScopeProvider.CreateCoreScope(autoComplete: true);
            scope.ReadLock(Constants.Locks.MemberTree);
            IQuery<IMember> query = Query<IMember>().Where(x => x.ContentTypeAlias == memberTypeAlias);
            return _memberRepository.Get(query);
        }

        /// <summary>
        /// Gets all Members for the MemberType id
        /// </summary>
        /// <param name="memberTypeId">Id of the MemberType</param>
        /// <returns><see cref="IEnumerable{IMember}"/></returns>
        public IEnumerable<IMember> GetMembersByMemberType(int memberTypeId)
        {
            using ICoreScope scope = ScopeProvider.CreateCoreScope(autoComplete: true);
            scope.ReadLock(Constants.Locks.MemberTree);
            IQuery<IMember> query = Query<IMember>().Where(x => x.ContentTypeId == memberTypeId);
            return _memberRepository.Get(query);
        }

        /// <summary>
        /// Gets all Members within the specified MemberGroup name
        /// </summary>
        /// <param name="memberGroupName">Name of the MemberGroup</param>
        /// <returns><see cref="IEnumerable{IMember}"/></returns>
        public IEnumerable<IMember> GetMembersByGroup(string memberGroupName)
        {
            using ICoreScope scope = ScopeProvider.CreateCoreScope(autoComplete: true);
            scope.ReadLock(Constants.Locks.MemberTree);
            return _memberRepository.GetByMemberGroup(memberGroupName);
        }

        /// <summary>
        /// Gets all Members with the ids specified
        /// </summary>
        /// <remarks>If no Ids are specified all Members will be retrieved</remarks>
        /// <param name="ids">Optional list of Member Ids</param>
        /// <returns><see cref="IEnumerable{IMember}"/></returns>
        public IEnumerable<IMember> GetAllMembers(params int[] ids)
        {
            using ICoreScope scope = ScopeProvider.CreateCoreScope(autoComplete: true);
            scope.ReadLock(Constants.Locks.MemberTree);
            return _memberRepository.GetMany(ids);
        }

        /// <inheritdoc />
        public Task<IEnumerable<IMember>> GetByKeysAsync(params Guid[] ids)
        {
            using ICoreScope scope = ScopeProvider.CreateCoreScope(autoComplete: true);
            scope.ReadLock(Constants.Locks.MemberTree);
            IQuery<IMember> query = Query<IMember>().Where(x => ids.Contains(x.Key));
            return Task.FromResult(_memberRepository.Get(query));
        }

        /// <summary>
        /// Finds Members based on their display name
        /// </summary>
        /// <param name="displayNameToMatch">Display name to match</param>
        /// <param name="pageIndex">Current page index</param>
        /// <param name="pageSize">Size of the page</param>
        /// <param name="totalRecords">Total number of records found (out)</param>
        /// <param name="matchType">The type of match to make as <see cref="StringPropertyMatchType"/>. Default is <see cref="StringPropertyMatchType.StartsWith"/></param>
        /// <returns><see cref="IEnumerable{IMember}"/></returns>
        public IEnumerable<IMember> FindMembersByDisplayName(string displayNameToMatch, long pageIndex, int pageSize, out long totalRecords, StringPropertyMatchType matchType = StringPropertyMatchType.StartsWith)
        {
            using ICoreScope scope = ScopeProvider.CreateCoreScope(autoComplete: true);
            scope.ReadLock(Constants.Locks.MemberTree);
            IQuery<IMember>? query = Query<IMember>();

            switch (matchType)
            {
                case StringPropertyMatchType.Exact:
                    query?.Where(member => string.Equals(member.Name, displayNameToMatch));
                    break;
                case StringPropertyMatchType.Contains:
                    query?.Where(member => member.Name != null && member.Name.Contains(displayNameToMatch));
                    break;
                case StringPropertyMatchType.StartsWith:
                    query?.Where(member => member.Name != null && member.Name.StartsWith(displayNameToMatch));
                    break;
                case StringPropertyMatchType.EndsWith:
                    query?.Where(member => member.Name != null && member.Name.EndsWith(displayNameToMatch));
                    break;
                case StringPropertyMatchType.Wildcard:
                    query?.Where(member => member.Name != null && member.Name.SqlWildcard(displayNameToMatch, TextColumnType.NVarchar));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(matchType)); // causes rollback // causes rollback
            }

            return _memberRepository.GetPage(query, pageIndex, pageSize, out totalRecords, null, Ordering.By("Name"));
        }

        /// <summary>
        /// Finds a list of <see cref="IMember"/> objects by a partial email string
        /// </summary>
        /// <param name="emailStringToMatch">Partial email string to match</param>
        /// <param name="pageIndex">Current page index</param>
        /// <param name="pageSize">Size of the page</param>
        /// <param name="totalRecords">Total number of records found (out)</param>
        /// <param name="matchType">The type of match to make as <see cref="StringPropertyMatchType"/>. Default is <see cref="StringPropertyMatchType.StartsWith"/></param>
        /// <returns><see cref="IEnumerable{IMember}"/></returns>
        public IEnumerable<IMember> FindByEmail(string emailStringToMatch, long pageIndex, int pageSize, out long totalRecords, StringPropertyMatchType matchType = StringPropertyMatchType.StartsWith)
        {
            using ICoreScope scope = ScopeProvider.CreateCoreScope(autoComplete: true);
            scope.ReadLock(Constants.Locks.MemberTree);
            IQuery<IMember>? query = Query<IMember>();

            switch (matchType)
            {
                case StringPropertyMatchType.Exact:
                    query?.Where(member => member.Email.Equals(emailStringToMatch));
                    break;
                case StringPropertyMatchType.Contains:
                    query?.Where(member => member.Email.Contains(emailStringToMatch));
                    break;
                case StringPropertyMatchType.StartsWith:
                    query?.Where(member => member.Email.StartsWith(emailStringToMatch));
                    break;
                case StringPropertyMatchType.EndsWith:
                    query?.Where(member => member.Email.EndsWith(emailStringToMatch));
                    break;
                case StringPropertyMatchType.Wildcard:
                    query?.Where(member => member.Email.SqlWildcard(emailStringToMatch, TextColumnType.NVarchar));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(matchType));
            }

            return _memberRepository.GetPage(query, pageIndex, pageSize, out totalRecords, null, Ordering.By("Email"));
        }

        /// <summary>
        /// Finds a list of <see cref="IMember"/> objects by a partial username
        /// </summary>
        /// <param name="login">Partial username to match</param>
        /// <param name="pageIndex">Current page index</param>
        /// <param name="pageSize">Size of the page</param>
        /// <param name="totalRecords">Total number of records found (out)</param>
        /// <param name="matchType">The type of match to make as <see cref="StringPropertyMatchType"/>. Default is <see cref="StringPropertyMatchType.StartsWith"/></param>
        /// <returns><see cref="IEnumerable{IMember}"/></returns>
        public IEnumerable<IMember> FindByUsername(string login, long pageIndex, int pageSize, out long totalRecords, StringPropertyMatchType matchType = StringPropertyMatchType.StartsWith)
        {
            using ICoreScope scope = ScopeProvider.CreateCoreScope(autoComplete: true);
            scope.ReadLock(Constants.Locks.MemberTree);
            IQuery<IMember>? query = Query<IMember>();

            switch (matchType)
            {
                case StringPropertyMatchType.Exact:
                    query?.Where(member => member.Username.Equals(login));
                    break;
                case StringPropertyMatchType.Contains:
                    query?.Where(member => member.Username.Contains(login));
                    break;
                case StringPropertyMatchType.StartsWith:
                    query?.Where(member => member.Username.StartsWith(login));
                    break;
                case StringPropertyMatchType.EndsWith:
                    query?.Where(member => member.Username.EndsWith(login));
                    break;
                case StringPropertyMatchType.Wildcard:
                    query?.Where(member => member.Username.SqlWildcard(login, TextColumnType.NVarchar));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(matchType));
            }

            return _memberRepository.GetPage(query, pageIndex, pageSize, out totalRecords, null, Ordering.By("LoginName"));
        }

        /// <summary>
        /// Gets a list of Members based on a property search
        /// </summary>
        /// <param name="propertyTypeAlias">Alias of the PropertyType to search for</param>
        /// <param name="value"><see cref="string"/> Value to match</param>
        /// <param name="matchType">The type of match to make as <see cref="StringPropertyMatchType"/>. Default is <see cref="StringPropertyMatchType.Exact"/></param>
        /// <returns><see cref="IEnumerable{IMember}"/></returns>
        public IEnumerable<IMember>? GetMembersByPropertyValue(string propertyTypeAlias, string value, StringPropertyMatchType matchType = StringPropertyMatchType.Exact)
        {
            using ICoreScope scope = ScopeProvider.CreateCoreScope(autoComplete: true);
            scope.ReadLock(Constants.Locks.MemberTree);
            IQuery<IMember> query;

            switch (matchType)
            {
                case StringPropertyMatchType.Exact:
                    query = Query<IMember>().Where(x => ((Member) x).PropertyTypeAlias == propertyTypeAlias && (((Member) x).LongStringPropertyValue!.SqlEquals(value, TextColumnType.NText) || ((Member) x).ShortStringPropertyValue!.SqlEquals(value, TextColumnType.NVarchar)));
                    break;
                case StringPropertyMatchType.Contains:
                    query = Query<IMember>().Where(x => ((Member) x).PropertyTypeAlias == propertyTypeAlias && (((Member) x).LongStringPropertyValue!.SqlContains(value, TextColumnType.NText) || ((Member) x).ShortStringPropertyValue!.SqlContains(value, TextColumnType.NVarchar)));
                    break;
                case StringPropertyMatchType.StartsWith:
                    query = Query<IMember>().Where(x => ((Member) x).PropertyTypeAlias == propertyTypeAlias && (((Member) x).LongStringPropertyValue.SqlStartsWith(value, TextColumnType.NText) || ((Member) x).ShortStringPropertyValue.SqlStartsWith(value, TextColumnType.NVarchar)));
                    break;
                case StringPropertyMatchType.EndsWith:
                    query = Query<IMember>().Where(x => ((Member) x).PropertyTypeAlias == propertyTypeAlias && (((Member) x).LongStringPropertyValue!.SqlEndsWith(value, TextColumnType.NText) || ((Member) x).ShortStringPropertyValue!.SqlEndsWith(value, TextColumnType.NVarchar)));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(matchType));
            }

            return _memberRepository.Get(query);
        }

        /// <summary>
        /// Gets a list of Members based on a property search
        /// </summary>
        /// <param name="propertyTypeAlias">Alias of the PropertyType to search for</param>
        /// <param name="value"><see cref="int"/> Value to match</param>
        /// <param name="matchType">The type of match to make as <see cref="StringPropertyMatchType"/>. Default is <see cref="StringPropertyMatchType.Exact"/></param>
        /// <returns><see cref="IEnumerable{IMember}"/></returns>
        public IEnumerable<IMember>? GetMembersByPropertyValue(string propertyTypeAlias, int value, ValuePropertyMatchType matchType = ValuePropertyMatchType.Exact)
        {
            using ICoreScope scope = ScopeProvider.CreateCoreScope(autoComplete: true);
            scope.ReadLock(Constants.Locks.MemberTree);
            IQuery<IMember> query;

            switch (matchType)
            {
                case ValuePropertyMatchType.Exact:
                    query = Query<IMember>().Where(x => ((Member) x).PropertyTypeAlias == propertyTypeAlias && ((Member) x).IntegerPropertyValue == value);
                    break;
                case ValuePropertyMatchType.GreaterThan:
                    query = Query<IMember>().Where(x => ((Member) x).PropertyTypeAlias == propertyTypeAlias && ((Member) x).IntegerPropertyValue > value);
                    break;
                case ValuePropertyMatchType.LessThan:
                    query = Query<IMember>().Where(x => ((Member) x).PropertyTypeAlias == propertyTypeAlias && ((Member) x).IntegerPropertyValue < value);
                    break;
                case ValuePropertyMatchType.GreaterThanOrEqualTo:
                    query = Query<IMember>().Where(x => ((Member) x).PropertyTypeAlias == propertyTypeAlias && ((Member) x).IntegerPropertyValue >= value);
                    break;
                case ValuePropertyMatchType.LessThanOrEqualTo:
                    query = Query<IMember>().Where(x => ((Member) x).PropertyTypeAlias == propertyTypeAlias && ((Member) x).IntegerPropertyValue <= value);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(matchType));
            }

            return _memberRepository.Get(query);
        }

        /// <summary>
        /// Gets a list of Members based on a property search
        /// </summary>
        /// <param name="propertyTypeAlias">Alias of the PropertyType to search for</param>
        /// <param name="value"><see cref="bool"/> Value to match</param>
        /// <returns><see cref="IEnumerable{IMember}"/></returns>
        public IEnumerable<IMember>? GetMembersByPropertyValue(string propertyTypeAlias, bool value)
        {
            using ICoreScope scope = ScopeProvider.CreateCoreScope(autoComplete: true);
            scope.ReadLock(Constants.Locks.MemberTree);
            IQuery<IMember> query = Query<IMember>().Where(x => ((Member) x).PropertyTypeAlias == propertyTypeAlias && ((Member) x).BoolPropertyValue == value);

            return _memberRepository.Get(query);
        }

        /// <summary>
        /// Gets a list of Members based on a property search
        /// </summary>
        /// <param name="propertyTypeAlias">Alias of the PropertyType to search for</param>
        /// <param name="value"><see cref="System.DateTime"/> Value to match</param>
        /// <param name="matchType">The type of match to make as <see cref="StringPropertyMatchType"/>. Default is <see cref="StringPropertyMatchType.Exact"/></param>
        /// <returns><see cref="IEnumerable{IMember}"/></returns>
        public IEnumerable<IMember>? GetMembersByPropertyValue(string propertyTypeAlias, DateTime value, ValuePropertyMatchType matchType = ValuePropertyMatchType.Exact)
        {
            using ICoreScope scope = ScopeProvider.CreateCoreScope(autoComplete: true);
            scope.ReadLock(Constants.Locks.MemberTree);
            IQuery<IMember> query;

            switch (matchType)
            {
                case ValuePropertyMatchType.Exact:
                    query = Query<IMember>().Where(x => ((Member) x).PropertyTypeAlias == propertyTypeAlias && ((Member) x).DateTimePropertyValue == value);
                    break;
                case ValuePropertyMatchType.GreaterThan:
                    query = Query<IMember>().Where(x => ((Member) x).PropertyTypeAlias == propertyTypeAlias && ((Member) x).DateTimePropertyValue > value);
                    break;
                case ValuePropertyMatchType.LessThan:
                    query = Query<IMember>().Where(x => ((Member) x).PropertyTypeAlias == propertyTypeAlias && ((Member) x).DateTimePropertyValue < value);
                    break;
                case ValuePropertyMatchType.GreaterThanOrEqualTo:
                    query = Query<IMember>().Where(x => ((Member) x).PropertyTypeAlias == propertyTypeAlias && ((Member) x).DateTimePropertyValue >= value);
                    break;
                case ValuePropertyMatchType.LessThanOrEqualTo:
                    query = Query<IMember>().Where(x => ((Member) x).PropertyTypeAlias == propertyTypeAlias && ((Member) x).DateTimePropertyValue <= value);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(matchType)); // causes rollback // causes rollback
            }

            // TODO: Since this is by property value, we need a GetByPropertyQuery on the repo!
            return _memberRepository.Get(query);
        }

        /// <summary>
        /// Checks if a Member with the id exists
        /// </summary>
        /// <param name="id">Id of the Member</param>
        /// <returns><c>True</c> if the Member exists otherwise <c>False</c></returns>
        public bool Exists(int id)
        {
            using ICoreScope scope = ScopeProvider.CreateCoreScope(autoComplete: true);
            scope.ReadLock(Constants.Locks.MemberTree);
            return _memberRepository.Exists(id);
        }

        /// <summary>
        /// Checks if a Member with the username exists
        /// </summary>
        /// <param name="username">Username to check</param>
        /// <returns><c>True</c> if the Member exists otherwise <c>False</c></returns>
        public bool Exists(string username)
        {
            using ICoreScope scope = ScopeProvider.CreateCoreScope(autoComplete: true);
            scope.ReadLock(Constants.Locks.MemberTree);
            return _memberRepository.Exists(username);
        }

        #endregion

        #region Save

        public void SetLastLogin(string username, DateTime date) => throw new NotImplementedException();

        /// <inheritdoc />
        public Attempt<OperationResult?> Save(IMember member, int userId = Constants.Security.SuperUserId)
        	=> Save(member, PublishNotificationSaveOptions.All, userId);

        public Attempt<OperationResult?> Save(IMember member, PublishNotificationSaveOptions publishNotificationSaveOptions, int userId = Constants.Security.SuperUserId)
        {
            // trimming username and email to make sure we have no trailing space
            member.Username = member.Username.Trim();
            member.Email = member.Email.Trim();

            EventMessages evtMsgs = EventMessagesFactory.Get();

            using ICoreScope scope = ScopeProvider.CreateCoreScope();
            MemberSavingNotification? savingNotification = null;
            if (publishNotificationSaveOptions.HasFlag(PublishNotificationSaveOptions.Saving))
            {
                savingNotification = new MemberSavingNotification(member, evtMsgs);
                if (scope.Notifications.PublishCancelable(savingNotification))
                {
                    scope.Complete();
                    return OperationResult.Attempt.Cancel(evtMsgs);
                }
            }

            if (string.IsNullOrWhiteSpace(member.Name))
            {
                throw new ArgumentException("Cannot save member with empty name.");
            }

            var previousUsername = _memberRepository.Get(member.Id)?.Username;

            scope.WriteLock(Constants.Locks.MemberTree);

            _memberRepository.Save(member);

            if (publishNotificationSaveOptions.HasFlag(PublishNotificationSaveOptions.Saved))
            {
                MemberSavedNotification memberSavedNotification = savingNotification is null
                    ? new MemberSavedNotification(member, evtMsgs)
                    : new MemberSavedNotification(member, evtMsgs).WithStateFrom(savingNotification);

                // If the user name has changed, populate the previous user name in the notification state, so the cache refreshers
                // have it available to clear the cache by the old name as well as the new.
                if (string.IsNullOrWhiteSpace(previousUsername) is false &&
                    string.Equals(previousUsername, member.Username, StringComparison.OrdinalIgnoreCase) is false)
                {
                    memberSavedNotification.State.Add(
                        MemberSavedNotification.PreviousUsernameStateKey,
                        new Dictionary<Guid, string> { { member.Key, previousUsername } });
                }

                scope.Notifications.Publish(memberSavedNotification);
            }

            Audit(AuditType.Save, userId, member.Id);

            scope.Complete();
            return OperationResult.Attempt.Succeed(evtMsgs);
        }

        public void Save(IMember member)
            => Save(member, Constants.Security.SuperUserId);

        /// <inheritdoc />
        public Attempt<OperationResult?> Save(IEnumerable<IMember> members, int userId = Constants.Security.SuperUserId)
        {
            IMember[] membersA = members.ToArray();

            EventMessages evtMsgs = EventMessagesFactory.Get();

            using ICoreScope scope = ScopeProvider.CreateCoreScope();
            var savingNotification = new MemberSavingNotification(membersA, evtMsgs);
            if (scope.Notifications.PublishCancelable(savingNotification))
            {
                scope.Complete();
                return OperationResult.Attempt.Cancel(evtMsgs);
            }

            scope.WriteLock(Constants.Locks.MemberTree);

            foreach (IMember member in membersA)
            {
                //trimming username and email to make sure we have no trailing space
                member.Username = member.Username.Trim();
                member.Email = member.Email.Trim();

                _memberRepository.Save(member);
            }

            scope.Notifications.Publish(new MemberSavedNotification(membersA, evtMsgs).WithStateFrom(savingNotification));

            Audit(AuditType.Save, userId, Constants.System.Root, "Save multiple Members");

            scope.Complete();
            return OperationResult.Attempt.Succeed(evtMsgs);
        }

        /// <inheritdoc />
        public void Save(IEnumerable<IMember> members)
            => Save(members, Constants.Security.SuperUserId);

        /// <inheritdoc/>
        /// <remarks>
        /// <para>
        ///     Note that in this optimized member save operation for use in the login process, where we only handle login related
        ///     properties, we aren't taking any locks. If we were updating "content" properties, that could have relations between each
        ///     other, we should following what we do for documents and lock.
        ///     But here we are just updating these system fields, and it's fine if they work in a "last one wins" fashion without locking.
        /// </para>
        /// <para>
        ///      Note also that we aren't calling "Audit" here (as well as to optimize performance, this is deliberate, because this is not
        ///      a full save operation on the member that we'd want to audit who made the changes via the backoffice or API; rather it's
        ///      just the member logging in as themselves).
        /// </para>
        /// <para>
        ///      We are though publishing notifications, to maintain backwards compatibility for any solutions using these for
        ///      processing following a member login.
        /// </para>
        /// <para>
        ///      These notification handlers will ensure that the records to umbracoLog are also added in the same way as they
        ///      are for a full save operation.
        /// </para>
        /// </remarks>
        public async Task UpdateLoginPropertiesAsync(IMember member)
        {
            EventMessages evtMsgs = EventMessagesFactory.Get();

            using ICoreScope scope = ScopeProvider.CreateCoreScope();
            var savingNotification = new MemberSavingNotification(member, evtMsgs);
            savingNotification.State.Add("LoginPropertiesOnly", true);
            if (scope.Notifications.PublishCancelable(savingNotification))
            {
                scope.Complete();
                return;
            }

            await _memberRepository.UpdateLoginPropertiesAsync(member);

            scope.Notifications.Publish(new MemberSavedNotification(member, evtMsgs).WithStateFrom(savingNotification));

            scope.Complete();
        }

        #endregion

        #region Delete

        /// <inheritdoc />
        public Attempt<OperationResult?> Delete(IMember member, int userId = Constants.Security.SuperUserId)
        {
            EventMessages evtMsgs = EventMessagesFactory.Get();

            using ICoreScope scope = ScopeProvider.CreateCoreScope();
            var deletingNotification = new MemberDeletingNotification(member, evtMsgs);
            if (scope.Notifications.PublishCancelable(deletingNotification))
            {
                scope.Complete();
                return OperationResult.Attempt.Cancel(evtMsgs);
            }

            scope.WriteLock(Constants.Locks.MemberTree);
            DeleteLocked(scope, member, evtMsgs, deletingNotification.State);

            Audit(AuditType.Delete, userId, member.Id);
            scope.Complete();

            return OperationResult.Attempt.Succeed(evtMsgs);
        }

        /// <inheritdoc />
        public void Delete(IMember member)
            => Delete(member, Constants.Security.SuperUserId);

        private void DeleteLocked(ICoreScope scope, IMember member, EventMessages evtMsgs, IDictionary<string, object?>? notificationState = null)
        {
            // a member has no descendants
            _memberRepository.Delete(member);
            scope.Notifications.Publish(new MemberDeletedNotification(member, evtMsgs).WithState(notificationState));

            // media files deleted by QueuingEventDispatcher
        }

        #endregion

        #region Roles

        public void AddRole(string roleName)
        {
            using ICoreScope scope = ScopeProvider.CreateCoreScope();
            scope.WriteLock(Constants.Locks.MemberTree);
            _memberGroupRepository.CreateIfNotExists(roleName);
            scope.Complete();
        }

        /// <summary>
        /// Returns a list of all member roles
        /// </summary>
        /// <returns>A list of member roles</returns>

        public IEnumerable<IMemberGroup> GetAllRoles()
        {
            using ICoreScope scope = ScopeProvider.CreateCoreScope(autoComplete: true);
            scope.ReadLock(Constants.Locks.MemberTree);
            return _memberGroupRepository.GetMany().Distinct();
        }

        /// <summary>
        /// Returns a list of all member roles for a given member ID
        /// </summary>
        /// <param name="memberId"></param>
        /// <returns>A list of member roles</returns>
        public IEnumerable<string> GetAllRoles(int memberId)
        {
            using ICoreScope scope = ScopeProvider.CreateCoreScope(autoComplete: true);
            scope.ReadLock(Constants.Locks.MemberTree);
            IEnumerable<IMemberGroup> result = _memberGroupRepository.GetMemberGroupsForMember(memberId);
            return result.Select(x => x.Name).WhereNotNull().Distinct();
        }

        public IEnumerable<string> GetAllRoles(string username)
        {
            using ICoreScope scope = ScopeProvider.CreateCoreScope(autoComplete: true);
            scope.ReadLock(Constants.Locks.MemberTree);
            IEnumerable<IMemberGroup> result = _memberGroupRepository.GetMemberGroupsForMember(username);
            return result.Where(x => x.Name != null).Select(x => x.Name).Distinct()!;
        }

        public IEnumerable<int> GetAllRolesIds()
        {
            using ICoreScope scope = ScopeProvider.CreateCoreScope(autoComplete: true);
            scope.ReadLock(Constants.Locks.MemberTree);
            return _memberGroupRepository.GetMany().Select(x => x.Id).Distinct();
        }

        public IEnumerable<int> GetAllRolesIds(int memberId)
        {
            using ICoreScope scope = ScopeProvider.CreateCoreScope(autoComplete: true);
            scope.ReadLock(Constants.Locks.MemberTree);
            IEnumerable<IMemberGroup> result = _memberGroupRepository.GetMemberGroupsForMember(memberId);
            return result.Select(x => x.Id).Distinct();
        }

        public IEnumerable<int> GetAllRolesIds(string username)
        {
            using ICoreScope scope = ScopeProvider.CreateCoreScope(autoComplete: true);
            scope.ReadLock(Constants.Locks.MemberTree);
            IEnumerable<IMemberGroup> result = _memberGroupRepository.GetMemberGroupsForMember(username);
            return result.Select(x => x.Id).Distinct();
        }

        public IEnumerable<IMember> GetMembersInRole(string roleName)
        {
            using ICoreScope scope = ScopeProvider.CreateCoreScope(autoComplete: true);
            scope.ReadLock(Constants.Locks.MemberTree);
            return _memberRepository.GetByMemberGroup(roleName);
        }

        public IEnumerable<IMember> FindMembersInRole(string roleName, string usernameToMatch, StringPropertyMatchType matchType = StringPropertyMatchType.StartsWith)
        {
            using ICoreScope scope = ScopeProvider.CreateCoreScope(autoComplete: true);
            scope.ReadLock(Constants.Locks.MemberTree);
            return _memberRepository.FindMembersInRole(roleName, usernameToMatch, matchType);
        }

        public bool DeleteRole(string roleName, bool throwIfBeingUsed)
        {
            using ICoreScope scope = ScopeProvider.CreateCoreScope();
            scope.WriteLock(Constants.Locks.MemberTree);

            if (throwIfBeingUsed)
            {
                // get members in role
                IEnumerable<IMember> membersInRole = _memberRepository.GetByMemberGroup(roleName);
                if (membersInRole.Any())
                {
                    throw new InvalidOperationException("The role " + roleName + " is currently assigned to members");
                }
            }

            IQuery<IMemberGroup> query = Query<IMemberGroup>().Where(g => g.Name == roleName);
            IMemberGroup[]? found = _memberGroupRepository.Get(query)?.ToArray();

            if (found is not null)
            {
                foreach (IMemberGroup memberGroup in found)
                {
                    _memberGroupService.Delete(memberGroup);
                }
            }

            scope.Complete();
            return found?.Length > 0;
        }

        public void AssignRole(string username, string roleName) => AssignRoles(new[] { username }, new[] { roleName });

        public void AssignRoles(string[] usernames, string[] roleNames)
        {
            using ICoreScope scope = ScopeProvider.CreateCoreScope();
            scope.WriteLock(Constants.Locks.MemberTree);
            var ids = _memberRepository.GetMemberIds(usernames);
            _memberGroupRepository.AssignRoles(ids, roleNames);
            scope.Notifications.Publish(new AssignedMemberRolesNotification(ids, roleNames));
            scope.Complete();
        }

        public void DissociateRole(string username, string roleName) => DissociateRoles(new[] { username }, new[] { roleName });

        public void DissociateRoles(string[] usernames, string[] roleNames)
        {
            using ICoreScope scope = ScopeProvider.CreateCoreScope();
            scope.WriteLock(Constants.Locks.MemberTree);
            var ids = _memberRepository.GetMemberIds(usernames);
            _memberGroupRepository.DissociateRoles(ids, roleNames);
            scope.Notifications.Publish(new RemovedMemberRolesNotification(ids, roleNames));
            scope.Complete();
        }

        public void AssignRole(int memberId, string roleName) => AssignRoles(new[] { memberId }, new[] { roleName });

        public void AssignRoles(int[] memberIds, string[] roleNames)
        {
            using ICoreScope scope = ScopeProvider.CreateCoreScope();
            scope.WriteLock(Constants.Locks.MemberTree);
            _memberGroupRepository.AssignRoles(memberIds, roleNames);
            scope.Notifications.Publish(new AssignedMemberRolesNotification(memberIds, roleNames));
            scope.Complete();
        }

        public void DissociateRole(int memberId, string roleName) => DissociateRoles(new[] { memberId }, new[] { roleName });

        public void DissociateRoles(int[] memberIds, string[] roleNames)
        {
            using ICoreScope scope = ScopeProvider.CreateCoreScope();
            scope.WriteLock(Constants.Locks.MemberTree);
            _memberGroupRepository.DissociateRoles(memberIds, roleNames);
            scope.Notifications.Publish(new RemovedMemberRolesNotification(memberIds, roleNames));
            scope.Complete();
        }

        public void ReplaceRoles(string[] usernames, string[] roleNames)
        {
            using ICoreScope scope = ScopeProvider.CreateCoreScope();
            scope.WriteLock(Constants.Locks.MemberTree);
            int[] ids = _memberRepository.GetMemberIds(usernames);
            _memberGroupRepository.ReplaceRoles(ids, roleNames);
            scope.Notifications.Publish(new AssignedMemberRolesNotification(ids, roleNames));
            scope.Complete();
        }

        public void ReplaceRoles(int[] memberIds, string[] roleNames)
        {
            using ICoreScope scope = ScopeProvider.CreateCoreScope();
            scope.WriteLock(Constants.Locks.MemberTree);
            _memberGroupRepository.ReplaceRoles(memberIds, roleNames);
            scope.Notifications.Publish(new AssignedMemberRolesNotification(memberIds, roleNames));
            scope.Complete();
        }

        #endregion

        #region Others

        // NOTE: at the time of writing we do not have MemberTreeChangeNotification to publish changes as a result of a data integrity
        //       check. we cannot support this feature until such notification exists.
        //       see the content or media services for implementation details if this is ever going to be a relevant feature for members.
        public ContentDataIntegrityReport CheckDataIntegrity(ContentDataIntegrityReportOptions options)
            => throw new InvalidOperationException("Data integrity checks are not (yet) implemented for members.");

        #endregion

        #region Private Methods

        private void Audit(AuditType type, int userId, int objectId, string? message = null) => _auditRepository.Save(new AuditItem(objectId, type, userId, ObjectTypes.GetName(UmbracoObjectTypes.Member), message));

        private IMember? GetMemberFromRepository(Guid id)
            => _idKeyMap.Value.GetIdForKey(id, UmbracoObjectTypes.Member) switch
            {
                { Success: false } => null,
                { Result: var intId } => _memberRepository.Get(intId),
            };
        #endregion

        #region Membership


        /// <summary>
        /// Exports a member.
        /// </summary>
        /// <remarks>
        /// This is internal for now and is used to export a member in the member editor,
        /// it will raise an event so that auditing logs can be created.
        /// </remarks>
        public MemberExportModel? ExportMember(Guid key)
        {
            using ICoreScope scope = ScopeProvider.CreateCoreScope(autoComplete: true);
            IQuery<IMember>? query = Query<IMember>().Where(x => x.Key == key);
            IMember? member = _memberRepository.Get(query)?.FirstOrDefault();

            if (member == null)
            {
                return null;
            }

            var model = new MemberExportModel
            {
                Id = member.Id,
                Key = member.Key,
                Name = member.Name,
                Username = member.Username,
                Email = member.Email,
                Groups = GetAllRoles(member.Id).ToList(),
                ContentTypeAlias = member.ContentTypeAlias,
                CreateDate = member.CreateDate,
                UpdateDate = member.UpdateDate,
                Properties = new List<MemberExportProperty>(GetPropertyExportItems(member))
            };

            scope.Notifications.Publish(new ExportedMemberNotification(member, model));

            return model;
        }

        private static IEnumerable<MemberExportProperty> GetPropertyExportItems(IMember member)
        {
            if (member == null)
            {
                throw new ArgumentNullException(nameof(member));
            }

            var exportProperties = new List<MemberExportProperty>();

            foreach (IProperty property in member.Properties)
            {
                var propertyExportModel = new MemberExportProperty
                {
                    Id = property.Id,
                    Alias = property.Alias,
                    Name = property.PropertyType.Name,
                    Value = property.GetValue(), // TODO: ignoring variants
                    CreateDate = property.CreateDate,
                    UpdateDate = property.UpdateDate
                };
                exportProperties.Add(propertyExportModel);
            }

            return exportProperties;
        }

        #endregion

        #region Content Types

        /// <summary>
        /// Delete Members of the specified MemberType id
        /// </summary>
        /// <param name="memberTypeId">Id of the MemberType</param>
        public void DeleteMembersOfType(int memberTypeId)
        {
            EventMessages evtMsgs = EventMessagesFactory.Get();

            // note: no tree to manage here
            using ICoreScope scope = ScopeProvider.CreateCoreScope();
            scope.WriteLock(Constants.Locks.MemberTree);

            // TODO: What about content that has the contenttype as part of its composition?
            IQuery<IMember>? query = Query<IMember>().Where(x => x.ContentTypeId == memberTypeId);

            IMember[]? members = _memberRepository.Get(query)?.ToArray();

            if (members is null)
            {
                return;
            }

            if (scope.Notifications.PublishCancelable(new MemberDeletingNotification(members, evtMsgs)))
            {
                scope.Complete();
                return;
            }

            foreach (IMember member in members)
            {
                // delete media
                // triggers the deleted event (and handles the files)
                DeleteLocked(scope, member, evtMsgs);
            }

            scope.Complete();
        }

        private IMemberType GetMemberType(ICoreScope scope, string memberTypeAlias)
        {
            if (memberTypeAlias == null)
            {
                throw new ArgumentNullException(nameof(memberTypeAlias));
            }

            if (string.IsNullOrWhiteSpace(memberTypeAlias))
            {
                throw new ArgumentException("Value can't be empty or consist only of white-space characters.", nameof(memberTypeAlias));
            }

            scope.ReadLock(Constants.Locks.MemberTypes);

            IMemberType? memberType = _memberTypeRepository.Get(memberTypeAlias);

            if (memberType == null)
            {
                throw new Exception($"No MemberType matching the passed in Alias: '{memberTypeAlias}' was found"); // causes rollback
            }

            return memberType;
        }

        private IMemberType GetMemberType(string memberTypeAlias)
        {
            if (memberTypeAlias == null)
            {
                throw new ArgumentNullException(nameof(memberTypeAlias));
            }

            if (string.IsNullOrWhiteSpace(memberTypeAlias))
            {
                throw new ArgumentException("Value can't be empty or consist only of white-space characters.", nameof(memberTypeAlias));
            }

            using ICoreScope scope = ScopeProvider.CreateCoreScope(autoComplete: true);
            return GetMemberType(scope, memberTypeAlias);
        }
        #endregion
    }
}
