﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Diagnostics;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;

namespace ssltest
{
    // SSL3/TLS protocol frames definitions . https://halfrost.com/https-extensions/
    //https://github.com/lafaspot/ja3_4java/blob/cf2c574eea699a72db57312627d1ca2ed8809131/src/main/java/com/lafaspot/ja3_4java/JA3Signature.java#L229
    public enum TlsContentType : byte
    {
        ChangeCipherSpec = 20,
        Alert = 21,
        Handshake = 22,
        AppData = 23
    }

    public enum CurveType : byte
    {
        CurveP256 = 23,
        CurveP384 = 24,
        CurveP521 = 25,
        X25519 = 29,
    }
    public enum EcPointFormat : byte
    {
        uncompressed = 0,
        ansiX962_compressed_prime = 1,
        ansiX962_compressed_char2 = 2,
    }
    public enum TlsHandshakeType : byte
    {
        HelloRequest = 0,
        ClientHello = 1,
        ServerHello = 2,
        NewSessionTicket = 4,
        EndOfEarlyData = 5,
        EncryptedExtensions = 8,
        Certificate = 11,
        ServerKeyExchange = 12,
        CertificateRequest = 13,
        ServerHelloDone = 14,
        CertificateVerify = 15,
        ClientKeyExchange = 16,
        Finished = 20,
        KeyEpdate = 24,
        MessageHash = 254
    }

    public enum TlsAlertLevel : byte
    {
        Warning = 1,
        Fatal = 2,
    }

    public enum TlsAlertDescription : byte
    {
        CloseNotify = 0, // warning
        UnexpectedMessage = 10, // error
        BadRecordMac = 20, // error
        DecryptionFailed = 21, // reserved
        RecordOverflow = 22, // error
        DecompressionFail = 30, // error
        HandshakeFailure = 40, // error
        BadCertificate = 42, // warning or error
        UnsupportedCert = 43, // warning or error
        CertificateRevoked = 44, // warning or error
        CertificateExpired = 45, // warning or error
        CertificateUnknown = 46, // warning or error
        IllegalParameter = 47, // error
        UnknownCA = 48, // error
        AccessDenied = 49, // error
        DecodeError = 50, // error
        DecryptError = 51, // error
        ExportRestriction = 60, // reserved
        ProtocolVersion = 70, // error
        InsuffientSecurity = 71, // error
        InternalError = 80, // error
        UserCanceled = 90, // warning or error
        NoRenegotiation = 100, // warning
        UnsupportedExt = 110, // error
    }
    // // Elliptic curve points 0x0a    // Elliptic curve point formats 0x0b
    public enum ExtensionType : ushort
    {
        server_name = 0,
        max_fragment_length = 1,
        client_certificate_url = 2,
        trusted_ca_keys = 3,
        truncated_hmac = 4,
        status_request = 5,
        user_mapping = 6,
        client_authz = 7,
        server_authz = 8,
        cert_type = 9,
        supported_groups = 10,//  Elliptic curve points
        ec_point_formats = 11, // Elliptic curve point formats
        srp = 12,
        signature_algorithms = 13,
        use_srtp = 14,
        heartbeat = 15,
        application_layer_protocol_negotiation = 16,
        status_request_v2 = 17,
        signed_certificate_timestamp = 18,
        client_certificate_type = 19,
        server_certificate_type = 20,
        padding = 21,
        encrypt_then_mac = 22,
        extended_master_secret = 23,
        token_binding = 24,
        cached_info = 25,
        tls_lts = 26,
        compress_certificate = 27,
        record_size_limit = 28,
        pwd_protect = 29,
        pwd_clear = 30,
        password_salt = 31,
        session_ticket = 35,
        pre_shared_key = 41,
        early_data = 42,
        supported_versions = 43,
        cookie = 44,
        psk_key_exchange_modes = 45,
        certificate_authorities = 47,
        oid_filters = 48,
        post_handshake_auth = 49,
        signature_algorithms_cert = 50,
        key_share = 51,
        extensionQUICTransportParams = 57,
        extensionCustom = 1234,  // not IANA assigned
        extensionNextProtoNeg = 13172, // not IANA assigned
        extensionApplicationSettings = 17513, // not IANA assigned
        extensionChannelID=30032,// not IANA assigned
        renegotiation_info = 65281
    }

    public struct TlsFrameHeader
    {
        public TlsContentType Type;
        public SslProtocols Version;
        public int Length;

        public override string ToString() => $"{Version}:{Type}[{Length}]";
    }



    public static class TlsFrameHelper
    {
        public const int HeaderSize = 5;

        [Flags]
        public enum ProcessingOptions
        {
            ServerName = 0x1,
            ApplicationProtocol = 0x2,
            Versions = 0x4,
            CipherSuites = 0x8,
            All = 0x7FFFFFFF,
        }

        [Flags]
        public enum ApplicationProtocolInfo
        {
            None = 0,
            Http11 = 1,
            Http2 = 2,
            Other = 128
        }

        public struct TlsFrameInfo
        {
            internal TlsCipherSuite[]? _ciphers;
            internal List<ExtensionType>? _extensions;
            internal List<CurveType>? _supportedgroups;
            internal List<EcPointFormat>? _ecPointFormats;
            public TlsFrameHeader Header;
            public TlsHandshakeType HandshakeType;
            public SslProtocols SupportedVersions;
            public string TargetName;
            public ApplicationProtocolInfo ApplicationProtocols;
            public TlsAlertDescription AlertDescription;
            public ReadOnlyMemory<TlsCipherSuite> TlsCipherSuites
            {
                get
                {
                    return _ciphers == null ? ReadOnlyMemory<TlsCipherSuite>.Empty : new ReadOnlyMemory<TlsCipherSuite>(_ciphers);
                }
            }

            public ReadOnlyMemory<ExtensionType> Extensions
            {
                get
                {
                    return _extensions == null
                        ? ReadOnlyMemory<ExtensionType>.Empty
                        : new ReadOnlyMemory<ExtensionType>(_extensions.ToArray());
                }
            }

            public ReadOnlyMemory<CurveType> SupportedGroups
            {
                get
                {
                    return _supportedgroups == null
                        ? ReadOnlyMemory<CurveType>.Empty
                        : new ReadOnlyMemory<CurveType>(_supportedgroups.ToArray());
                }
            }

            public ReadOnlyMemory<EcPointFormat> EcPointFormats
            {
                get
                {
                    return _ecPointFormats == null
                        ? ReadOnlyMemory<EcPointFormat>.Empty
                        : new ReadOnlyMemory<EcPointFormat>(_ecPointFormats.ToArray());
                }
            }

            public (string, string,string) getSig()
            {
                StringBuilder sb = new StringBuilder();
                List<string> s2b = new List<string>();
                sb.Append((int)Header.Version);
                sb.Append(",");
                if (_ciphers != null)
                {
                    sb.Append(string.Join("-", _ciphers.Select(r => (int)r)));
                    s2b.Add(string.Join("-", _ciphers.Select(r => r.ToString())));
                }
                sb.Append(",");
                if (_extensions != null)
                {
                    sb.Append(string.Join("-", _extensions.Select(r => (int)r)));
                    s2b.Add(string.Join("-", _extensions.Select(r => r.ToString())));
                }
                sb.Append(",");
                if (_supportedgroups != null)
                {
                    sb.Append(string.Join("-", _supportedgroups.Select(r => (int)r)));
                    s2b.Add(string.Join("-", _supportedgroups.Select(r => r.ToString())));
                }
                sb.Append(",");
                if (_ecPointFormats != null)
                {
                    sb.Append(string.Join("-", _ecPointFormats.Select(r => (int)r)));
                    s2b.Add(string.Join("-", _ecPointFormats.Select(r => r.ToString())));
                }
                s2b.Add(Header.Version.ToString());
                String str = sb.ToString();
                using var md5 = MD5.Create();
                var result = md5.ComputeHash(Encoding.ASCII.GetBytes(str));
                var strResult = BitConverter.ToString(result);
                var sig = strResult.Replace("-", "").ToLower();
                return (str, sig, string.Join('|', s2b));
            }


           

            public override string ToString()
            {

                if (Header.Type == TlsContentType.Handshake)
                {
                    if (HandshakeType == TlsHandshakeType.ClientHello)
                    {

                        return $"{Header.Version}:{HandshakeType}[{Header.Length}] TargetName='{TargetName}' SupportedVersion='{SupportedVersions}' ApplicationProtocols='{ApplicationProtocols}'-->sig:{getSig()}";
                    }
                    else if (HandshakeType == TlsHandshakeType.ServerHello)
                    {
                        return $"{Header.Version}:{HandshakeType}[{Header.Length}] SupportedVersion='{SupportedVersions}' ApplicationProtocols='{ApplicationProtocols}'";
                    }
                    else
                    {
                        return $"{Header.Version}:{HandshakeType}[{Header.Length}] SupportedVersion='{SupportedVersions}'";
                    }
                }
                else
                {
                    return $"{Header.Version}:{Header.Type}[{Header.Length}]";
                }
            }
        }

        public delegate bool HelloExtensionCallback(ref TlsFrameInfo info, ExtensionType type, ReadOnlySpan<byte> extensionsData);

        private static byte[] s_protocolMismatch13 = new byte[] { (byte)TlsContentType.Alert, 3, 4, 0, 2, 2, 70 };
        private static byte[] s_protocolMismatch12 = new byte[] { (byte)TlsContentType.Alert, 3, 3, 0, 2, 2, 70 };
        private static byte[] s_protocolMismatch11 = new byte[] { (byte)TlsContentType.Alert, 3, 2, 0, 2, 2, 70 };
        private static byte[] s_protocolMismatch10 = new byte[] { (byte)TlsContentType.Alert, 3, 1, 0, 2, 2, 70 };
        private static byte[] s_protocolMismatch30 = new byte[] { (byte)TlsContentType.Alert, 3, 0, 0, 2, 2, 40 };

        private const int UInt24Size = 3;
        private const int RandomSize = 32;
        private const int OpaqueType1LengthSize = sizeof(byte);
        private const int OpaqueType2LengthSize = sizeof(ushort);
        private const int ProtocolVersionMajorOffset = 0;
        private const int ProtocolVersionMinorOffset = 1;
        private const int ProtocolVersionSize = 2;
        private const int ProtocolVersionTlsMajorValue = 3;

        // Per spec "AllowUnassigned flag MUST be set". See comment above DecodeString() for more details.
        private static readonly IdnMapping s_idnMapping = new IdnMapping() { AllowUnassigned = true };
        private static readonly Encoding s_encoding = Encoding.GetEncoding("utf-8", new EncoderExceptionFallback(), new DecoderExceptionFallback());

        public static bool TryGetFrameHeader(ReadOnlySpan<byte> frame, ref TlsFrameHeader header)
        {
            bool result = frame.Length > 4;

            if (frame.Length >= 1)
            {
                header.Type = (TlsContentType)frame[0];

                if (frame.Length >= 3)
                {
                    // SSLv3, TLS or later
                    if (frame[1] == 3)
                    {
                        if (frame.Length > 4)
                        {
                            header.Length = ((frame[3] << 8) | frame[4]);
                        }

                        header.Version = TlsMinorVersionToProtocol(frame[10]);
                    }
                    else
                    {
                        header.Length = -1;
                        header.Version = SslProtocols.None;
                    }
                }
            }

            return result;
        }

        // Returns frame size e.g. header + content
        public static int GetFrameSize(ReadOnlySpan<byte> frame)
        {
            if (frame.Length < 5 || frame[1] < 3)
            {
                return -1;
            }

            return ((frame[3] << 8) | frame[4]) + HeaderSize;
        }

        /**
   * Values to account for GREASE (Generate Random Extensions And Sustain Extensibility) as described here:
   * https://tools.ietf.org/html/draft-davidben-tls-grease-01.
   */
        private static int[] GREASE = new int[] { 0x0a0a, 0x1a1a, 0x2a2a, 0x3a3a, 0x4a4a, 0x5a5a, 0x6a6a, 0x7a7a, 0x8a8a, 0x9a9a, 0xaaaa, 0xbaba,
            0xcaca, 0xdada, 0xeaea, 0xfafa };

        /**
     * Check if TLS protocols cipher, extension, named groups, signature algorithms and version values match GREASE values. <blockquote
     * cite="https://tools.ietf.org/html/draft-ietf-tls-grease"> GREASE (Generate Random Extensions And Sustain Extensibility), a mechanism to prevent
     * extensibility failures in the TLS ecosystem. It reserves a set of TLS protocol values that may be advertised to ensure peers correctly handle
     * unknown values </blockquote>
     *
     * @param value value to be checked against GREASE values
     * @return false if value matches GREASE value, true otherwise
     * @see <a href="https://tools.ietf.org/html/draft-ietf-tls-grease">draft-ietf-tls-grease</a>
     */
        private static bool isNotGrease(int value)
        {
            for (int i = 0; i < GREASE.Length; i++)
            {
                if (value == GREASE[i])
                {
                    return false;
                }
            }

            return true;
        }

        // This function will try to parse TLS hello frame and fill details in provided info structure.
        // If frame was fully processed without any error, function returns true.
        // Otherwise it returns false and info may have partial data.
        // It is OK to call it again if more data becomes available.
        // It is also possible to limit what information is processed.
        // If callback delegate is provided, it will be called on ALL extensions.
        public static bool TryGetFrameInfo(ReadOnlySpan<byte> frame, ref TlsFrameInfo info, ProcessingOptions options = ProcessingOptions.All, HelloExtensionCallback? callback = null)
        {
            const int HandshakeTypeOffset = 5;
            if (frame.Length < HeaderSize)
            {
                return false;
            }

            // This will not fail since we have enough data.
            bool gotHeader = TryGetFrameHeader(frame, ref info.Header);
            Debug.Assert(gotHeader);

            info.SupportedVersions = info.Header.Version;

            if (info.Header.Type == TlsContentType.Alert)
            {
                TlsAlertLevel level = default;
                TlsAlertDescription description = default;
                if (TryGetAlertInfo(frame, ref level, ref description))
                {
                    info.AlertDescription = description;
                    return true;
                }

                return false;
            }

            if (info.Header.Type != TlsContentType.Handshake || frame.Length <= HandshakeTypeOffset)
            {
                return false;
            }

            info.HandshakeType = (TlsHandshakeType)frame[HandshakeTypeOffset];

            // Check if we have full frame.
            bool isComplete = frame.Length >= HeaderSize + info.Header.Length;

            if (((int)info.Header.Version >= (int)SslProtocols.Tls) &&
                (info.HandshakeType == TlsHandshakeType.ClientHello || info.HandshakeType == TlsHandshakeType.ServerHello))
            {
                if (!TryParseHelloFrame(frame.Slice(HeaderSize), ref info, options, callback))
                {
                    isComplete = false;
                }
            }

            return isComplete;
        }

        // This is similar to TryGetFrameInfo but it will only process SNI.
        // It returns TargetName as string or NULL if SNI is missing or parsing error happened.
        public static string? GetServerName(ReadOnlySpan<byte> frame)
        {
            TlsFrameInfo info = default;
            if (!TryGetFrameInfo(frame, ref info, ProcessingOptions.ServerName))
            {
                return null;
            }

            return info.TargetName;
        }

        // This function will parse TLS Alert message and it will return alert level and description.
        public static bool TryGetAlertInfo(ReadOnlySpan<byte> frame, ref TlsAlertLevel level, ref TlsAlertDescription description)
        {
            if (frame.Length < 7 || frame[0] != (byte)TlsContentType.Alert)
            {
                return false;
            }

            level = (TlsAlertLevel)frame[5];
            description = (TlsAlertDescription)frame[6];

            return true;
        }

        private static byte[] CreateProtocolVersionAlert(SslProtocols version) =>
            version switch
            {
                SslProtocols.Tls13 => s_protocolMismatch13,
                SslProtocols.Tls12 => s_protocolMismatch12,
                SslProtocols.Tls11 => s_protocolMismatch11,
                SslProtocols.Tls => s_protocolMismatch10,
#pragma warning disable 0618
                SslProtocols.Ssl3 => s_protocolMismatch30,
#pragma warning restore 0618
                _ => Array.Empty<byte>(),
            };

        public static byte[] CreateAlertFrame(SslProtocols version, TlsAlertDescription reason)
        {
            if (reason == TlsAlertDescription.ProtocolVersion)
            {
                return CreateProtocolVersionAlert(version);
            }
            else if ((int)version > (int)SslProtocols.Tls)
            {
                // Create TLS1.2 alert
                byte[] buffer = new byte[] { (byte)TlsContentType.Alert, 3, 3, 0, 2, 2, (byte)reason };
                switch (version)
                {
                    case SslProtocols.Tls13:
                        buffer[2] = 4;
                        break;
                    case SslProtocols.Tls11:
                        buffer[2] = 2;
                        break;
                    case SslProtocols.Tls:
                        buffer[2] = 1;
                        break;
                }

                return buffer;
            }

            return Array.Empty<byte>();
        }

        private static bool TryParseHelloFrame(ReadOnlySpan<byte> sslHandshake, ref TlsFrameInfo info, ProcessingOptions options, HelloExtensionCallback? callback)
        {
            // https://tools.ietf.org/html/rfc6101#section-5.6
            // struct {
            //     HandshakeType msg_type;    /* handshake type */
            //     uint24 length;             /* bytes in message */
            //     select (HandshakeType) {
            //         ...
            //         case client_hello: ClientHello;
            //         case server_hello: ServerHello;
            //         ...
            //     } body;
            // } Handshake;
            const int HandshakeTypeOffset = 0;
            const int HelloLengthOffset = HandshakeTypeOffset + sizeof(TlsHandshakeType);
            const int HelloOffset = HelloLengthOffset + UInt24Size;

            if (sslHandshake.Length < HelloOffset ||
                ((TlsHandshakeType)sslHandshake[HandshakeTypeOffset] != TlsHandshakeType.ClientHello &&
                 (TlsHandshakeType)sslHandshake[HandshakeTypeOffset] != TlsHandshakeType.ServerHello))
            {
                return false;
            }

            int helloLength = ReadUInt24BigEndian(sslHandshake.Slice(HelloLengthOffset));
            ReadOnlySpan<byte> helloData = sslHandshake.Slice(HelloOffset);

            if (helloData.Length < helloLength)
            {
                return false;
            }

            // ProtocolVersion may be different from frame header.
            if (helloData[ProtocolVersionMajorOffset] == ProtocolVersionTlsMajorValue)
            {
                info.SupportedVersions |= TlsMinorVersionToProtocol(helloData[ProtocolVersionMinorOffset]);
            }

            return (TlsHandshakeType)sslHandshake[HandshakeTypeOffset] == TlsHandshakeType.ClientHello ?
                        TryParseClientHello(helloData.Slice(0, helloLength), ref info, options, callback) :
                        TryParseServerHello(helloData.Slice(0, helloLength), ref info, options, callback);
        }

        private static bool TryParseClientHello(ReadOnlySpan<byte> clientHello, ref TlsFrameInfo info, ProcessingOptions options, HelloExtensionCallback? callback)
        {
            // Basic structure: https://tools.ietf.org/html/rfc6101#section-5.6.1.2
            // Extended structure: https://tools.ietf.org/html/rfc3546#section-2.1
            // struct {
            //     ProtocolVersion client_version; // 2x uint8
            //     Random random; // 32 bytes
            //     SessionID session_id; // opaque type
            //     CipherSuite cipher_suites<2..2^16-1>; // opaque type
            //     CompressionMethod compression_methods<1..2^8-1>; // opaque type
            //     Extension client_hello_extension_list<0..2^16-1>;
            // } ClientHello;

            ReadOnlySpan<byte> p = SkipBytes(clientHello, ProtocolVersionSize + RandomSize);

            // Skip SessionID (max size 32 => size fits in 1 byte)
            p = SkipOpaqueType1(p);

            if (options.HasFlag(ProcessingOptions.CipherSuites))
            {
                TryGetCipherSuites(p, ref info);
            }
            // Skip cipher suites (max size 2^16-1 => size fits in 2 bytes)
            p = SkipOpaqueType2(p);

            // Skip compression methods (max size 2^8-1 => size fits in 1 byte)
            p = SkipOpaqueType1(p);

            // is invalid structure or no extensions?
            if (p.IsEmpty)
            {
                return false;
            }

            // client_hello_extension_list (max size 2^16-1 => size fits in 2 bytes)
            int extensionListLength = BinaryPrimitives.ReadUInt16BigEndian(p);
            p = SkipBytes(p, sizeof(ushort));
            if (extensionListLength != p.Length)
            {
                return false;
            }

            return TryParseHelloExtensions(p, ref info, options, callback);
        }

        private static bool TryParseServerHello(ReadOnlySpan<byte> serverHello, ref TlsFrameInfo info, ProcessingOptions options, HelloExtensionCallback? callback)
        {
            // Basic structure: https://tools.ietf.org/html/rfc6101#section-5.6.1.3
            // Extended structure: https://tools.ietf.org/html/rfc3546#section-2.2
            // struct {
            //   ProtocolVersion server_version;
            //   Random random;
            //   SessionID session_id;
            //   CipherSuite cipher_suite;
            //   CompressionMethod compression_method;
            //   Extension server_hello_extension_list<0..2^16-1>;
            // }
            // ServerHello;
            const int CipherSuiteLength = 2;
            const int CompressionMethiodLength = 1;

            ReadOnlySpan<byte> p = SkipBytes(serverHello, ProtocolVersionSize + RandomSize);
            // Skip SessionID (max size 32 => size fits in 1 byte)
            p = SkipOpaqueType1(p);
            p = SkipBytes(p, CipherSuiteLength + CompressionMethiodLength);

            // is invalid structure or no extensions?
            if (p.IsEmpty)
            {
                return false;
            }

            // client_hello_extension_list (max size 2^16-1 => size fits in 2 bytes)
            int extensionListLength = BinaryPrimitives.ReadUInt16BigEndian(p);
            p = SkipBytes(p, sizeof(ushort));
            if (extensionListLength != p.Length)
            {
                return false;
            }

            return TryParseHelloExtensions(p, ref info, options, callback);
        }

        // This is common for ClientHello and ServerHello.
        private static bool TryParseHelloExtensions(ReadOnlySpan<byte> extensions, ref TlsFrameInfo info, ProcessingOptions options, HelloExtensionCallback? callback)
        {
            const int ExtensionHeader = 4;
            bool isComplete = true;

            while (extensions.Length >= ExtensionHeader)
            {
                ExtensionType extensionType = (ExtensionType)BinaryPrimitives.ReadUInt16BigEndian(extensions);
                extensions = SkipBytes(extensions, sizeof(ushort));

                ushort extensionLength = BinaryPrimitives.ReadUInt16BigEndian(extensions);
                extensions = SkipBytes(extensions, sizeof(ushort));
                if (extensions.Length < extensionLength)
                {
                    isComplete = false;
                    break;
                }

                ReadOnlySpan<byte> extensionData = extensions.Slice(0, extensionLength);

                if (extensionType == ExtensionType.server_name && options.HasFlag(ProcessingOptions.ServerName))
                {
                    if (!TryGetSniFromServerNameList(extensionData, out string? sni))
                    {
                        return false;
                    }

                    info.TargetName = sni!;
                }
                else if (extensionType == ExtensionType.supported_versions && options.HasFlag(ProcessingOptions.Versions))
                {
                    if (!TryGetSupportedVersionsFromExtension(extensionData, out SslProtocols versions))
                    {
                        return false;
                    }

                    info.SupportedVersions |= versions;
                }
                else if (extensionType == ExtensionType.application_layer_protocol_negotiation && options.HasFlag(ProcessingOptions.ApplicationProtocol))
                {
                    if (!TryGetApplicationProtocolsFromExtension(extensionData, out ApplicationProtocolInfo alpn))
                    {
                        return false;
                    }

                    info.ApplicationProtocols |= alpn;
                }

                if (extensionType == ExtensionType.supported_groups)
                {
                    if (!TryGetSupportedGroups(extensionData, ref info))
                    {
                        return false;
                    }

                }

                if (extensionType == ExtensionType.ec_point_formats)
                {
                    if (!TryGetEcPointsFormats(extensionData, ref info))
                    {
                        return false;
                    }
                }

                info._extensions ??= new List<ExtensionType>();
                if (isNotGrease((int)extensionType))
                {
                    info._extensions.Add(extensionType);
                }
                callback?.Invoke(ref info, extensionType, extensionData);
                extensions = extensions.Slice(extensionLength);
            }

            return isComplete;
        }

        private static bool TryGetSniFromServerNameList(ReadOnlySpan<byte> serverNameListExtension, out string? sni)
        {
            // https://tools.ietf.org/html/rfc3546#section-3.1
            // struct {
            //     ServerName server_name_list<1..2^16-1>
            // } ServerNameList;
            // ServerNameList is an opaque type (length of sufficient size for max data length is prepended)
            const int ServerNameListOffset = sizeof(ushort);
            sni = null;

            if (serverNameListExtension.Length < ServerNameListOffset)
            {
                return false;
            }

            int serverNameListLength = BinaryPrimitives.ReadUInt16BigEndian(serverNameListExtension);
            ReadOnlySpan<byte> serverNameList = serverNameListExtension.Slice(ServerNameListOffset);

            if (serverNameListLength != serverNameList.Length)
            {
                return false;
            }

            ReadOnlySpan<byte> serverName = serverNameList.Slice(0, serverNameListLength);

            sni = GetSniFromServerName(serverName, out bool invalid);
            return invalid == false;
        }

        private static string? GetSniFromServerName(ReadOnlySpan<byte> serverName, out bool invalid)
        {
            // https://tools.ietf.org/html/rfc3546#section-3.1
            // struct {
            //     NameType name_type;
            //     select (name_type) {
            //         case host_name: HostName;
            //     } name;
            // } ServerName;
            // ServerName is an opaque type (length of sufficient size for max data length is prepended)
            const int NameTypeOffset = 0;
            const int HostNameStructOffset = NameTypeOffset + sizeof(NameType);
            if (serverName.Length < HostNameStructOffset)
            {
                invalid = true;
                return null;
            }

            // Following can underflow but it is ok due to equality check below
            NameType nameType = (NameType)serverName[NameTypeOffset];
            ReadOnlySpan<byte> hostNameStruct = serverName.Slice(HostNameStructOffset);
            if (nameType != NameType.HostName)
            {
                invalid = true;
                return null;
            }

            return GetSniFromHostNameStruct(hostNameStruct, out invalid);
        }

        private static string? GetSniFromHostNameStruct(ReadOnlySpan<byte> hostNameStruct, out bool invalid)
        {
            // https://tools.ietf.org/html/rfc3546#section-3.1
            // HostName is an opaque type (length of sufficient size for max data length is prepended)
            const int HostNameLengthOffset = 0;
            const int HostNameOffset = HostNameLengthOffset + sizeof(ushort);

            int hostNameLength = BinaryPrimitives.ReadUInt16BigEndian(hostNameStruct);
            ReadOnlySpan<byte> hostName = hostNameStruct.Slice(HostNameOffset);
            if (hostNameLength != hostName.Length)
            {
                invalid = true;
                return null;
            }

            invalid = false;
            return DecodeString(hostName);
        }

        private static bool TryGetSupportedVersionsFromExtension(ReadOnlySpan<byte> extensionData, out SslProtocols protocols)
        {
            // https://tools.ietf.org/html/rfc8446#section-4.2.1
            // struct {
            // select(Handshake.msg_type) {
            //  case client_hello:
            //    ProtocolVersion versions<2..254 >;
            //
            //  case server_hello: /* and HelloRetryRequest */
            //    ProtocolVersion selected_version;
            // };
            const int VersionListLengthOffset = 0;
            const int VersionListNameOffset = VersionListLengthOffset + sizeof(byte);
            const int VersionLength = 2;

            protocols = SslProtocols.None;

            byte supportedVersionLength = extensionData[VersionListLengthOffset];
            extensionData = extensionData.Slice(VersionListNameOffset);

            if (extensionData.Length != supportedVersionLength)
            {
                return false;
            }

            // Get list of protocols we support.I nore the rest.
            while (extensionData.Length >= VersionLength)
            {
                if (extensionData[ProtocolVersionMajorOffset] == ProtocolVersionTlsMajorValue)
                {
                    protocols |= TlsMinorVersionToProtocol(extensionData[ProtocolVersionMinorOffset]);
                }

                extensionData = extensionData.Slice(VersionLength);
            }

            return true;
        }

        private static bool TryGetEcPointsFormats(ReadOnlySpan<byte> extensionData, ref TlsFrameInfo info)
        {
            const int VersionListLengthOffset = 0;
            const int VersionListNameOffset = VersionListLengthOffset + sizeof(byte);
            byte supportedVersionLength = extensionData[VersionListLengthOffset];
            ReadOnlySpan<byte> alpnList = extensionData.Slice(VersionListNameOffset);

            if (alpnList.Length != supportedVersionLength)
            {
                return false;
            }

            info._ecPointFormats = new List<EcPointFormat>();
            foreach (var code in alpnList)
            {
                if (code > 2) continue;
                EcPointFormat t = (EcPointFormat)code;
                info._ecPointFormats.Add(t);
            }

            return true;
        }
        private static bool TryGetSupportedGroups(ReadOnlySpan<byte> extensionData, ref TlsFrameInfo info)
        {
            const int SupportedGroupsOffset = 1;
            const int SupportedGroupsListOffset = SupportedGroupsOffset + sizeof(byte);
            byte supportedVersionLength = extensionData[SupportedGroupsOffset];
            extensionData = extensionData.Slice(SupportedGroupsListOffset);
            if (extensionData.Length != supportedVersionLength)
            {
                return false;
            }

            info._supportedgroups = new List<CurveType>();
            const int VersionLength = 2;
            while (extensionData.Length >= VersionLength)
            {
                if (extensionData[ProtocolVersionMajorOffset] == 0)
                {
                    CurveType t = (CurveType)extensionData[ProtocolVersionMinorOffset];
                    info._supportedgroups.Add(t);
                }

                extensionData = extensionData.Slice(VersionLength);
            }
            return true;
        }

        private static bool TryGetApplicationProtocolsFromExtension(ReadOnlySpan<byte> extensionData, out ApplicationProtocolInfo alpn)
        {
            // https://tools.ietf.org/html/rfc7301#section-3.1
            // opaque ProtocolName<1..2 ^ 8 - 1 >;
            //
            // struct {
            //   ProtocolName protocol_name_list<2..2^16-1>
            // }
            // ProtocolNameList;
            const int AlpnListLengthOffset = 0;
            const int AlpnListOffset = AlpnListLengthOffset + sizeof(short);

            alpn = ApplicationProtocolInfo.None;

            if (extensionData.Length < AlpnListOffset)
            {
                return false;
            }

            int AlpnListLength = BinaryPrimitives.ReadUInt16BigEndian(extensionData);
            ReadOnlySpan<byte> alpnList = extensionData.Slice(AlpnListOffset);
            if (AlpnListLength != alpnList.Length)
            {
                return false;
            }

            while (!alpnList.IsEmpty)
            {
                byte protocolLength = alpnList[0];
                if (alpnList.Length < protocolLength + 1)
                {
                    return false;
                }

                ReadOnlySpan<byte> protocol = alpnList.Slice(1, protocolLength);
                if (protocolLength == 2)
                {
                    if (protocol.SequenceEqual(SslApplicationProtocol.Http2.Protocol.Span))
                    {
                        alpn |= ApplicationProtocolInfo.Http2;
                    }
                    else
                    {
                        alpn |= ApplicationProtocolInfo.Other;
                    }
                }
                else if (protocolLength == SslApplicationProtocol.Http11.Protocol.Length &&
                         protocol.SequenceEqual(SslApplicationProtocol.Http11.Protocol.Span))
                {
                    alpn |= ApplicationProtocolInfo.Http11;
                }
                else
                {
                    alpn |= ApplicationProtocolInfo.Other;
                }

                alpnList = alpnList.Slice(protocolLength + 1);
            }

            return true;
        }

        private static bool TryGetCipherSuites(ReadOnlySpan<byte> bytes, ref TlsFrameInfo info)
        {
            if (bytes.Length < OpaqueType2LengthSize)
            {
                return false;
            }

            ushort length = BinaryPrimitives.ReadUInt16BigEndian(bytes);
            if (bytes.Length < OpaqueType2LengthSize + length)
            {
                return false;
            }

            bytes = bytes.Slice(OpaqueType2LengthSize, length);
            int count = length / 2;

            info._ciphers = new TlsCipherSuite[count];
            for (int i = 0; i < count; i++)
            {
                TlsCipherSuite t = (TlsCipherSuite)BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(i * 2, 2));
                if (isNotGrease((int)t))
                {
                    info._ciphers[i] = t;
                }
            }

            return true;
        }

        private static SslProtocols TlsMinorVersionToProtocol(byte value)
        {
            return value switch
            {
                4 => SslProtocols.Tls13,
                3 => SslProtocols.Tls12,
                2 => SslProtocols.Tls11,
                1 => SslProtocols.Tls,
#pragma warning disable 0618
                0 => SslProtocols.Ssl3,
#pragma warning restore 0618
                _ => SslProtocols.None,
            };
        }


        private static string? DecodeString(ReadOnlySpan<byte> bytes)
        {
            // https://tools.ietf.org/html/rfc3546#section-3.1
            // Per spec:
            //   If the hostname labels contain only US-ASCII characters, then the
            //   client MUST ensure that labels are separated only by the byte 0x2E,
            //   representing the dot character U+002E (requirement 1 in section 3.1
            //   of [IDNA] notwithstanding). If the server needs to match the HostName
            //   against names that contain non-US-ASCII characters, it MUST perform
            //   the conversion operation described in section 4 of [IDNA], treating
            //   the HostName as a "query string" (i.e. the AllowUnassigned flag MUST
            //   be set). Note that IDNA allows labels to be separated by any of the
            //   Unicode characters U+002E, U+3002, U+FF0E, and U+FF61, therefore
            //   servers MUST accept any of these characters as a label separator.  If
            //   the server only needs to match the HostName against names containing
            //   exclusively ASCII characters, it MUST compare ASCII names case-
            //   insensitively.

            string idnEncodedString;
            try
            {
                idnEncodedString = s_encoding.GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                return null;
            }

            try
            {
                return s_idnMapping.GetUnicode(idnEncodedString);
            }
            catch (ArgumentException)
            {
                // client has not done IDN mapping
                return idnEncodedString;
            }
        }

        private static int ReadUInt24BigEndian(ReadOnlySpan<byte> bytes)
        {
            return (bytes[0] << 16) | (bytes[1] << 8) | bytes[2];
        }

        private static ReadOnlySpan<byte> SkipBytes(ReadOnlySpan<byte> bytes, int numberOfBytesToSkip)
        {
            return (numberOfBytesToSkip < bytes.Length) ? bytes.Slice(numberOfBytesToSkip) : ReadOnlySpan<byte>.Empty;
        }

        // Opaque type is of structure:
        //   - length (minimum number of bytes to hold the max value)
        //   - data (length bytes)
        // We will only use opaque types which are of max size: 255 (length = 1) or 2^16-1 (length = 2).
        // We will call them SkipOpaqueType`length`
        private static ReadOnlySpan<byte> SkipOpaqueType1(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length < OpaqueType1LengthSize)
            {
                return ReadOnlySpan<byte>.Empty;
            }

            byte length = bytes[0];
            int totalBytes = OpaqueType1LengthSize + length;

            return SkipBytes(bytes, totalBytes);
        }

        private static ReadOnlySpan<byte> SkipOpaqueType2(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length < OpaqueType2LengthSize)
            {
                return ReadOnlySpan<byte>.Empty;
            }

            ushort length = BinaryPrimitives.ReadUInt16BigEndian(bytes);
            int totalBytes = OpaqueType2LengthSize + length;

            return SkipBytes(bytes, totalBytes);
        }

        private enum NameType : byte
        {
            HostName = 0x00
        }
    }
}