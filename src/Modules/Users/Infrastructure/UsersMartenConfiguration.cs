using Casino.Modules.Users.Domain;
using Marten;

namespace Casino.Modules.Users.Infrastructure;

public static class UsersMartenConfiguration
{
    public static void Register(StoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.Schema.For<UserProfile>().Identity(profile => profile.Id);
    }
}
