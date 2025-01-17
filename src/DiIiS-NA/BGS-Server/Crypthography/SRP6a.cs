﻿//Blizzless Project 2022
//Blizzless Project 2022 
using DiIiS_NA.Core.Extensions;
//Blizzless Project 2022 
using DiIiS_NA.LoginServer.AccountsSystem;
//Blizzless Project 2022 
using System;
//Blizzless Project 2022 
using System.Linq;
//Blizzless Project 2022 
using System.Numerics;
//Blizzless Project 2022 
using System.Security.Cryptography;
//Blizzless Project 2022 
using System.Text;

namespace DiIiS_NA.LoginServer.Crypthography
{
	public class SRP6a
	{
		// The following is a description of SRP-6 and 6a, the latest versions of SRP:
		// ---------------------------------------------------------------------------
		//   N	A large safe prime (N = 2q+1, where q is prime)
		//		All arithmetic is done modulo N.
		//   g	A generator modulo N
		//   k	Multiplier parameter (k = H(N, g) in SRP-6a, k = 3 for legacy SRP-6)
		//   s	User's salt
		//   I	Username
		//   p	Cleartext Password
		//   H()  One-way hash function
		//   ^	(Modular) Exponentiation
		//   u	Random scrambling parameter
		//   a,b  Secret ephemeral values
		//   A,B  Public ephemeral values
		//   x	Private key (derived from p and s)
		//   v	Password verifier
		// ---------------------------------------------------------------------------
		// specification: http://srp.stanford.edu/design.html
		// article: http://en.wikipedia.org/wiki/Secure_Remote_Password_protocol
		// contains code from tomrus88 (https://github.com/tomrus88/d3proto/blob/master/Core/SRP.cs

		private static readonly SHA256 H = SHA256.Create(); // H() One-way hash function.

		/// <summary>
		/// Account used within SRP6-a authentication.
		/// </summary>
		public Account Account { get; private set; }

		
		public string IdentitySalt { get; private set; }

		/// <summary>
		///  K = H(S) - Shared, strong session key.
		/// </summary>
		public byte[] SessionKey { get; private set; }

		/// <summary>
		/// Server's secret ephemeral value.
		/// </summary>
		private readonly BigInteger b;

		/// <summary>
		/// Server's public ephemeral value
		/// </summary>
		private readonly BigInteger B;

		/// <summary>
		/// Returns server's logon challenge message.
		/// command = 0 
		/// byte identity salt [32]; - identity-salt - generated by hashing account email [static value per account] (skipped when command == 1)
		/// byte password salt[32]; - account-salt - generated on account creation [static value per account]
		/// byte serverChallenge[128]; - changes every login - server's public ephemeral value (B)
		/// byte secondChallenge[128]; - extra challenge
		/// </summary>
		public byte[] LogonChallenge { get; private set; }

		/// <summary>
		/// Returns logon proof.
		/// command == 3 - server sends proof of session key to client
		/// byte M_server[32] - server's proof of session key.
		/// byte secondProof[128]; // for veriyfing second challenge.
		/// </summary>
		public byte[] LogonProof { get; private set; }

		public SRP6a(Account account)
		{
			this.Account = account;
			this.IdentitySalt = H.ComputeHash(Encoding.ASCII.GetBytes(this.Account.Email)).ToHexString(); 
			// calculate server's public ephemeral value.
			this.b = GetRandomBytes(128).ToBigInteger(); // server's secret ephemeral value.
			var gModb = BigInteger.ModPow(g, b, N); // pow(g, b, N)
			var k = H.ComputeHash(new byte[0].Concat(N.ToArray()).Concat(g.ToArray()).ToArray()).ToBigInteger(); // Multiplier parameter (k = H(N, g) in SRP-6a
			this.B = BigInteger.Remainder((this.Account.PasswordVerifier.ToBigInteger() * k) + gModb, N); // B = (k * v + pow(g, b, N)) % N

			// cook the logon challenge message
			this.LogonChallenge = new byte[0]
				.Concat(new byte[] { 0 }) // command = 0
				.Concat(this.IdentitySalt.ToByteArray()) // identity-salt - generated by hashing account email.
				.Concat(this.Account.Salt) // account-salt - generated on account creation.
				.Concat(B.ToArray(128)) // server's public ephemeral value (B)
				.Concat(b.ToArray(128)) // second challenge
				.ToArray();
		}

		/// <summary>
		/// Calculates password verifier for given email, password and salt.
		/// </summary>
		/// <param name="email">The account email.</param>
		/// <param name="password">The password.</param>
		/// <param name="salt">The generated salt.</param>
		/// <returns></returns>
		public static byte[] CalculatePasswordVerifierForAccount(string email, string password, byte[] salt)
		{
			// x = H(s, p) -> s: randomly choosen salt
			// v = g^x (computes password verifier)

			// TODO: it seems hashing identity-salt + password bugs for passwords with >11 chars or so.
			// we need to get rid of that identity-salt in pBytes /raist.

			var identitySalt = H.ComputeHash(Encoding.ASCII.GetBytes(email)).ToHexString(); 
			var pBytes = H.ComputeHash(Encoding.ASCII.GetBytes(identitySalt.ToUpper() + ":" + password.ToUpper())); // p (identitySalt + password)
			var x = H.ComputeHash(new byte[0].Concat(salt).Concat(pBytes).ToArray()).ToBigInteger(); // x = H(s, p)

			return BigInteger.ModPow(g, x, N).ToArray(128);
		}

		
		public bool Verify(byte[] A_bytes, byte[] M_client, byte[] seed)
		{
			var A = A_bytes.ToBigInteger(); // client's public ephemeral
			var u = H.ComputeHash(new byte[0].Concat(A_bytes).Concat(B.ToArray(128)).ToArray()).ToBigInteger(); // Random scrambling parameter - u = H(A, B)

			var S_s = BigInteger.ModPow(A * BigInteger.ModPow(this.Account.PasswordVerifier.ToBigInteger(), u, N), b, N); // calculate server session key - S = (Av^u) ^ b	 
			this.SessionKey = Calc_K(S_s.ToArray(128)); //  K = H(S) - Shared, strong session key.
			byte[] K_s = this.SessionKey;

			var hashgxorhashN = Hash_g_and_N_and_xor_them().ToBigInteger(); // H(N) ^ H(g)
			var hashedIdentitySalt = H.ComputeHash(Encoding.ASCII.GetBytes(this.IdentitySalt)); // H(I)

			var M = H.ComputeHash(new byte[0] // verify client M_client - H(H(N) ^ H(g), H(I), s, A, B, K_c)
				.Concat(hashgxorhashN.ToArray(32))
				.Concat(hashedIdentitySalt)
				.Concat(this.Account.Salt.ToArray())
				.Concat(A_bytes)
				.Concat(B.ToArray(128))
				.Concat(K_s)
				.ToArray());

			// We can basically move m_server, secondproof and logonproof calculation behind the M.CompareTo(M_client) check, but as we have an option DisablePasswordChecks 
			// which allows authentication without the correct password, they should be also calculated for wrong-passsword auths. /raist.

			// calculate server proof of session key
			var M_server = H.ComputeHash(new byte[0] // M_server = H(A, M_client, K)
				.Concat(A_bytes)
				.Concat(M_client)
				.Concat(K_s)
				.ToArray());

			// cook logon proof message.
			LogonProof = new byte[0]
				.Concat(new byte[] { 3 }) // command = 3 - server sends proof of session key to client
				.Concat(M_server) // server's proof of session key
				.Concat(B.ToArray(128)) // second proof
				.ToArray();

			if (M.CompareTo(M_client)) // successful authentication session.
				return true;
			else // authentication failed because of invalid credentals.
				return false;
		}

		public static byte[] GetRandomBytes(int count)
		{
			var rnd = new Random();
			var result = new byte[count];
			rnd.NextBytes(result);
			return result;
		}

		//  Interleave SHA256 Key
		private byte[] Calc_K(byte[] S)
		{
			var K = new byte[64];

			var half_S = new byte[64];

			for (int i = 0; i < 64; ++i)
				half_S[i] = S[i * 2];

			var p1 = H.ComputeHash(half_S);

			for (int i = 0; i < 32; ++i)
				K[i * 2] = p1[i];

			for (int i = 0; i < 64; ++i)
				half_S[i] = S[i * 2 + 1];

			var p2 = H.ComputeHash(half_S);

			for (int i = 0; i < 32; ++i)
				K[i * 2 + 1] = p2[i];

			return K;
		}

		/// <summary>
		/// H(N) ^ H(g)
		/// </summary>
		/// <returns>byte[]</returns>
		private byte[] Hash_g_and_N_and_xor_them()
		{
			var hash_N = H.ComputeHash(N.ToArray());
			var hash_g = H.ComputeHash(g.ToArray());

			for (var i = 0; i < 32; ++i)
				hash_N[i] ^= hash_g[i];

			return hash_N;
		}

		/// <summary>
		/// A generator modulo N
		/// </summary>
		private static readonly BigInteger g = new byte[] { 0x02 }.ToBigInteger();

		/// <summary>
		/// A large safe prime (N = 2q+1, where q is prime)
		/// </summary>
		private static readonly BigInteger N = new byte[]
		{
			0xAB, 0x24, 0x43, 0x63, 0xA9, 0xC2, 0xA6, 0xC3, 0x3B, 0x37, 0xE4, 0x61, 0x84, 0x25, 0x9F, 0x8B,
			0x3F, 0xCB, 0x8A, 0x85, 0x27, 0xFC, 0x3D, 0x87, 0xBE, 0xA0, 0x54, 0xD2, 0x38, 0x5D, 0x12, 0xB7,
			0x61, 0x44, 0x2E, 0x83, 0xFA, 0xC2, 0x21, 0xD9, 0x10, 0x9F, 0xC1, 0x9F, 0xEA, 0x50, 0xE3, 0x09,
			0xA6, 0xE5, 0x5E, 0x23, 0xA7, 0x77, 0xEB, 0x00, 0xC7, 0xBA, 0xBF, 0xF8, 0x55, 0x8A, 0x0E, 0x80,
			0x2B, 0x14, 0x1A, 0xA2, 0xD4, 0x43, 0xA9, 0xD4, 0xAF, 0xAD, 0xB5, 0xE1, 0xF5, 0xAC, 0xA6, 0x13,
			0x1C, 0x69, 0x78, 0x64, 0x0B, 0x7B, 0xAF, 0x9C, 0xC5, 0x50, 0x31, 0x8A, 0x23, 0x08, 0x01, 0xA1,
			0xF5, 0xFE, 0x31, 0x32, 0x7F, 0xE2, 0x05, 0x82, 0xD6, 0x0B, 0xED, 0x4D, 0x55, 0x32, 0x41, 0x94,
			0x29, 0x6F, 0x55, 0x7D, 0xE3, 0x0F, 0x77, 0x19, 0xE5, 0x6C, 0x30, 0xEB, 0xDE, 0xF6, 0xA7, 0x86
		}.ToBigInteger();
	}
}
