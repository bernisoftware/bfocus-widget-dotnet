using System;

namespace Bfocus.Widget
{
    /// <summary>
    /// Payload do usuário (<c>user=</c> e <c>X-bFocus-Widget-User</c>): JSON compacto nesta ordem de
    /// chaves, campos vazios omitidos, <c>locale</c> sempre presente, e base64 padrão sobre UTF-8.
    /// </summary>
    public static class UserPayload
    {
        public static string Json(BFocusUser user, BFocusCustomer customer, string? userHash, string locale)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));
            if (customer == null) throw new ArgumentNullException(nameof(customer));

            var c = new JsonObjectBuilder()
                .AddIfNotEmpty("externalId", customer.ExternalId)
                .AddIfNotEmpty("name", customer.Name)
                .AddIfNotEmpty("document", customer.Document)
                .AddIfNotEmpty("email", customer.Email)
                .AddIfNotEmpty("phone", customer.Phone)
                .AddIfNotEmpty("website", customer.Website);

            return new JsonObjectBuilder()
                .AddIfNotEmpty("externalId", user.ExternalId)
                .AddIfNotEmpty("name", user.Name)
                .AddIfNotEmpty("email", user.Email)
                .AddIfNotEmpty("phone", user.Phone)
                .AddIfNotEmpty("userHash", userHash)
                .Add("locale", locale)
                .AddRaw("customer", c.ToString())
                .ToString();
        }

        /// <summary>Base64 padrão (com + / =). O embed decodifica com atob sobre UTF-8.</summary>
        public static string Base64(string json) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));
    }
}
