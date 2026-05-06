using UnityEngine;

namespace Ecopoly.Data
{
    /// <summary>
    /// Holds Vivox Developer Portal credentials.
    /// Assign values in the Inspector on SO_VivoxSettings.asset.
    /// WARNING: The TokenKey is a secret — never ship it in a production build.
    ///          For production, implement IVivoxTokenProvider and generate tokens server-side.
    /// </summary>
    [CreateAssetMenu(fileName = "SO_VivoxSettings", menuName = "Ecopoly/VivoxSettings")]
    public class VivoxSettings : ScriptableObject
    {
        [Header("Vivox Developer Portal Credentials")]
        [Tooltip("e.g. https://unity.vivox.com/appconfig/90718-ecopo-23542")]
        public string Server      = "";

        [Tooltip("e.g. mtu1xp.vivox.com")]
        public string Domain      = "";

        [Tooltip("e.g. 90718-ecopo-23542")]
        public string TokenIssuer = "";

        /// <summary>
        /// Secret key used for local token generation.
        /// Safe for development / internal testing only.
        /// </summary>
        [Tooltip("Vivox secret key — development/testing only.")]
        public string TokenKey    = "";
    }
}
