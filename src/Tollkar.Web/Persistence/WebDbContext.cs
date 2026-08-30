using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Tollkar.Web.Authentication;

namespace Tollkar.Web.Persistence;

public sealed class WebDbContext(DbContextOptions<WebDbContext> options)
    : IdentityUserContext<TollkarUser>(options);
