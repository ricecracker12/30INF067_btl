using Microsoft.AspNetCore.Authorization;

namespace SocialApp.SharedKernel.Authorization;

/// <summary>Yêu cầu "người gọi có mã quyền <paramref name="Permission"/>" — do PermissionHandler (C2) xét.</summary>
public sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;
