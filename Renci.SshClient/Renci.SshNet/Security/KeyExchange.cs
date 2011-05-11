﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using Renci.SshNet.Common;
using Renci.SshNet.Compression;
using Renci.SshNet.Messages;
using Renci.SshNet.Messages.Transport;

namespace Renci.SshNet.Security
{
    /// <summary>
    /// Represents base class for different key exchange algorithm implementations
    /// </summary>
    public abstract class KeyExchange : Algorithm, IDisposable
    {
        private Type _clientCipherType;

        private Type _serverCipherType;

        private Type _cientHmacAlgorithmType;

        private Type _serverHmacAlgorithmType;

        private Type _compressionType;

        private Type _decompressionType;

        /// <summary>
        /// Gets or sets the session.
        /// </summary>
        /// <value>
        /// The session.
        /// </value>
        protected Session Session { get; private set; }

        /// <summary>
        /// Gets or sets key exchange shared key.
        /// </summary>
        /// <value>
        /// The shared key.
        /// </value>
        public BigInteger SharedKey { get; protected set; }

        private byte[] _exchangeHash;
        /// <summary>
        /// Gets the exchange hash.
        /// </summary>
        /// <value>The exchange hash.</value>
        public byte[] ExchangeHash
        {
            get
            {
                if (this._exchangeHash == null)
                {
                    this._exchangeHash = this.CalculateHash();
                }
                return this._exchangeHash;
            }
        }

        /// <summary>
        /// Starts key exchange algorithm
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="message">Key exchange init message.</param>
        public virtual void Start(Session session, KeyExchangeInitMessage message)
        {
            this.Session = session;

            this.SendMessage(session.ClientInitMessage);

            //  Determine encryption algorithm
            var clientEncryptionAlgorithmName = (from b in session.ConnectionInfo.Encryptions.Keys
                                                 from a in message.EncryptionAlgorithmsClientToServer
                                                 where a == b
                                                 select a).FirstOrDefault();

            if (string.IsNullOrEmpty(clientEncryptionAlgorithmName))
            {
                throw new SshConnectionException("Client encryption algorithm not found", DisconnectReason.KeyExchangeFailed);
            }

            //  Determine encryption algorithm
            var serverDecryptionAlgorithmName = (from b in session.ConnectionInfo.Encryptions.Keys
                                                 from a in message.EncryptionAlgorithmsServerToClient
                                                 where a == b
                                                 select a).FirstOrDefault();
            if (string.IsNullOrEmpty(serverDecryptionAlgorithmName))
            {
                throw new SshConnectionException("Server decryption algorithm not found", DisconnectReason.KeyExchangeFailed);
            }

            //  Determine client hmac algorithm
            var clientHmacAlgorithmName = (from b in session.ConnectionInfo.HmacAlgorithms.Keys
                                           from a in message.MacAlgorithmsClientToServer
                                           where a == b
                                           select a).FirstOrDefault();
            if (string.IsNullOrEmpty(clientHmacAlgorithmName))
            {
                throw new SshConnectionException("Server HMAC algorithm not found", DisconnectReason.KeyExchangeFailed);
            }

            //  Determine server hmac algorithm
            var serverHmacAlgorithmName = (from b in session.ConnectionInfo.HmacAlgorithms.Keys
                                           from a in message.MacAlgorithmsServerToClient
                                           where a == b
                                           select a).FirstOrDefault();
            if (string.IsNullOrEmpty(serverHmacAlgorithmName))
            {
                throw new SshConnectionException("Server HMAC algorithm not found", DisconnectReason.KeyExchangeFailed);
            }

            //  Determine compression algorithm
            var compressionAlgorithmName = (from b in session.ConnectionInfo.CompressionAlgorithms.Keys
                                            from a in message.CompressionAlgorithmsClientToServer
                                            where a == b
                                            select a).FirstOrDefault();
            if (string.IsNullOrEmpty(compressionAlgorithmName))
            {
                throw new SshConnectionException("Compression algorithm not found", DisconnectReason.KeyExchangeFailed);
            }

            //  Determine decompression algorithm
            var decompressionAlgorithmName = (from b in session.ConnectionInfo.CompressionAlgorithms.Keys
                                              from a in message.CompressionAlgorithmsServerToClient
                                              where a == b
                                              select a).FirstOrDefault();
            if (string.IsNullOrEmpty(decompressionAlgorithmName))
            {
                throw new SshConnectionException("Decompression algorithm not found", DisconnectReason.KeyExchangeFailed);
            }

            this._clientCipherType = session.ConnectionInfo.Encryptions[clientEncryptionAlgorithmName];
            this._serverCipherType = session.ConnectionInfo.Encryptions[clientEncryptionAlgorithmName];
            this._cientHmacAlgorithmType = session.ConnectionInfo.HmacAlgorithms[clientHmacAlgorithmName];
            this._serverHmacAlgorithmType = session.ConnectionInfo.HmacAlgorithms[serverHmacAlgorithmName];
            this._compressionType = session.ConnectionInfo.CompressionAlgorithms[compressionAlgorithmName];
            this._decompressionType = session.ConnectionInfo.CompressionAlgorithms[decompressionAlgorithmName];
        }

        /// <summary>
        /// Finishes key exchange algorithm.
        /// </summary>
        public virtual void Finish()
        {
            //  Validate hash
            if (this.ValidateExchangeHash())
            {
                this.SendMessage(new NewKeysMessage());
            }
            else
            {
                throw new SshConnectionException("Key exchange negotiation failed.", DisconnectReason.KeyExchangeFailed);
            }
        }

        /// <summary>
        /// Creates the server side cipher to use.
        /// </summary>
        /// <returns></returns>
        public Cipher CreateServerCipher()
        {
            //  Resolve Session ID
            var sessionId = this.Session.SessionId ?? this.ExchangeHash;

            //  Create server cipher
            var serverCipher = this._serverCipherType.CreateInstance<Cipher>();

            //  Calculate server to client initial IV
            var serverVector = this.Hash(this.GenerateSessionKey(this.SharedKey, this.ExchangeHash, 'B', sessionId));

            //  Calculate server to client encryption
            var serverKey = this.Hash(this.GenerateSessionKey(this.SharedKey, this.ExchangeHash, 'D', sessionId));

            serverKey = this.GenerateSessionKey(this.SharedKey, this.ExchangeHash, serverKey, serverCipher.KeySize / 8);

            serverCipher.Init(serverKey, serverVector);

            return serverCipher;
        }

        /// <summary>
        /// Creates the client side cipher to use.
        /// </summary>
        /// <returns></returns>
        public Cipher CreateClientCipher()
        {
            //  Resolve Session ID
            var sessionId = this.Session.SessionId ?? this.ExchangeHash;

            //  Create client cipher
            var clientCipher = this._clientCipherType.CreateInstance<Cipher>();

            //  Calculate client to server initial IV
            var clientVector = this.Hash(this.GenerateSessionKey(this.SharedKey, this.ExchangeHash, 'A', sessionId));

            //  Calculate client to server encryption
            var clientKey = this.Hash(this.GenerateSessionKey(this.SharedKey, this.ExchangeHash, 'C', sessionId));

            clientKey = this.GenerateSessionKey(this.SharedKey, this.ExchangeHash, clientKey, clientCipher.KeySize / 8);

            clientCipher.Init(clientKey, clientVector);

            return clientCipher;
        }

        /// <summary>
        /// Creates the server side hash algorithm to use.
        /// </summary>
        /// <returns></returns>
        public HMac CreateServerHash()
        {
            //  Resolve Session ID
            var sessionId = this.Session.SessionId ?? this.ExchangeHash;

            //  Create server HMac
            var serverHMac = this._serverHmacAlgorithmType.CreateInstance<HMac>();

            serverHMac.Init(this.Hash(this.GenerateSessionKey(this.SharedKey, this.ExchangeHash, 'F', sessionId)));

            return serverHMac;
        }

        /// <summary>
        /// Creates the client side hash algorithm to use.
        /// </summary>
        /// <returns></returns>
        public HMac CreateClientHash()
        {
            //  Resolve Session ID
            var sessionId = this.Session.SessionId ?? this.ExchangeHash;

            //  Create client HMac
            var clientHMac = this._cientHmacAlgorithmType.CreateInstance<HMac>();

            clientHMac.Init(this.Hash(this.GenerateSessionKey(this.SharedKey, this.ExchangeHash, 'E', sessionId)));

            return clientHMac;
        }

        /// <summary>
        /// Creates the compression algorithm to use to deflate data.
        /// </summary>
        /// <returns></returns>
        public Compressor CreateCompressor()
        {
            if (this._compressionType == null)
                return null;

            var compressor = this._compressionType.CreateInstance<Compressor>();

            compressor.Init(this.Session);

            return compressor;
        }

        /// <summary>
        /// Creates the compression algorithm to use to inflate data.
        /// </summary>
        /// <returns></returns>
        public Compressor CreateDecompressor()
        {
            if (this._compressionType == null)
                return null;

            var decompressor = this._decompressionType.CreateInstance<Compressor>();

            decompressor.Init(this.Session);

            return decompressor;
        }


        /// <summary>
        /// Validates the exchange hash.
        /// </summary>
        /// <returns>true if exchange hash is valid; otherwise false.</returns>
        protected abstract bool ValidateExchangeHash();

        /// <summary>
        /// Calculates key exchange hash value.
        /// </summary>
        /// <returns>Key exchange hash.</returns>
        protected abstract byte[] CalculateHash();

        /// <summary>
        /// Hashes the specified data bytes.
        /// </summary>
        /// <param name="hashBytes">Data to hash.</param>
        /// <returns>Hashed bytes</returns>
        protected virtual byte[] Hash(IEnumerable<byte> hashBytes)
        {
            using (var md = new SHA1CryptoServiceProvider())
            {
                using (var cs = new System.Security.Cryptography.CryptoStream(System.IO.Stream.Null, md, System.Security.Cryptography.CryptoStreamMode.Write))
                {
                    var hashData = hashBytes.ToArray();
                    cs.Write(hashData, 0, hashData.Length);
                }
                return md.Hash;
            }
        }

        /// <summary>
        /// Sends SSH message to the server
        /// </summary>
        /// <param name="message">The message.</param>
        protected void SendMessage(Message message)
        {
            this.Session.SendMessage(message);
        }

        /// <summary>
        /// Generates the session key.
        /// </summary>
        /// <param name="sharedKey">The shared key.</param>
        /// <param name="exchangeHash">The exchange hash.</param>
        /// <param name="key">The key.</param>
        /// <param name="size">The size.</param>
        /// <returns></returns>
        private byte[] GenerateSessionKey(BigInteger sharedKey, byte[] exchangeHash, IEnumerable<byte> key, int size)
        {
            var result = new List<byte>(key);
            while (size > result.Count)
            {
                result.AddRange(this.Hash(new _SessionKeyAdjustment
                {
                    SharedKey = sharedKey,
                    ExcahngeHash = exchangeHash,
                    Key = key,
                }.GetBytes()));
            }

            return result.ToArray();
        }

        /// <summary>
        /// Generates the session key.
        /// </summary>
        /// <param name="sharedKey">The shared key.</param>
        /// <param name="exchangeHash">The exchange hash.</param>
        /// <param name="p">The p.</param>
        /// <param name="sessionId">The session id.</param>
        /// <returns></returns>
        private IEnumerable<byte> GenerateSessionKey(BigInteger sharedKey, IEnumerable<byte> exchangeHash, char p, IEnumerable<byte> sessionId)
        {
            return new _SessionKeyGeneration
            {
                SharedKey = sharedKey,
                ExchangeHash = exchangeHash,
                Char = p,
                SessionId = sessionId,
            }.GetBytes();
        }

        private class _SessionKeyGeneration : SshData
        {
            public BigInteger SharedKey { get; set; }
            public IEnumerable<byte> ExchangeHash { get; set; }
            public char Char { get; set; }
            public IEnumerable<byte> SessionId { get; set; }

            protected override void LoadData()
            {
                throw new NotImplementedException();
            }

            protected override void SaveData()
            {
                this.Write(this.SharedKey);
                this.Write(this.ExchangeHash);
                this.Write((byte)this.Char);
                this.Write(this.SessionId);
            }
        }

        private class _SessionKeyAdjustment : SshData
        {
            public BigInteger SharedKey { get; set; }
            public IEnumerable<byte> ExcahngeHash { get; set; }
            public IEnumerable<byte> Key { get; set; }

            protected override void LoadData()
            {
                throw new NotImplementedException();
            }

            protected override void SaveData()
            {
                this.Write(this.SharedKey);
                this.Write(this.ExcahngeHash);
                this.Write(this.Key);
            }
        }

        #region IDisposable Members

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged ResourceMessages.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);

            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases unmanaged and - optionally - managed resources
        /// </summary>
        /// <param name="disposing"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged ResourceMessages.</param>
        protected virtual void Dispose(bool disposing)
        {
        }

        /// <summary>
        /// Releases unmanaged resources and performs other cleanup operations before the
        /// <see cref="KeyExchange"/> is reclaimed by garbage collection.
        /// </summary>
        ~KeyExchange()
        {
            // Do not re-create Dispose clean-up code here.
            // Calling Dispose(false) is optimal in terms of
            // readability and maintainability.
            Dispose(false);
        }

        #endregion
    }
}
