using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using Sabertooth.Lexicon;
using WebSharp;

namespace Sabertooth.HTTP {
	public static class HTTP {
		public const string Newline = "\r\n";
	}
	public abstract class HTTPObject : IStreamable{


		public bool IsLoaded{ get{return true;}}
		public HTTPObject() {

		}

		public abstract byte[] GetBytes ();
		public abstract int GetSize ();
		public abstract void StreamTo (Stream S);
	}
	public class Instruction {
		public readonly string Key;
		public string Value;
		private List<string> Options = new List<string>();
		public Instruction(string key, string value) {
			Key = key;Value = value;
		}
		public enum Connection {KeepAlive, Close}
		internal static Instruction ConnectionClose = new Instruction ("Connection", "close");
		internal static Instruction ConnectionKeepAlive = new Instruction ("Connection", "keep-alive");
		internal static Instruction ContentLength(int length) {
			return new Instruction ("Content-Length", length.ToString());
		}
		internal static Instruction ContentType(MIME type) {
			return new Instruction ("Content-Type", type.ToString());
		}
		public static Instruction Authenticate(string realm) {
			return new Instruction ("WWW-Authenticate", String.Format("Basic realm=\"{0}\"", realm));
		}
		public static Instruction SetCookie(SetCookie C) {
			return new Instruction ("Set-Cookie", C.ToString());
		}
		public static Instruction LastModified(DateTime D) {
			return new Instruction ("Last-Modified", D.ToString("R"));
		}
		public static Instruction ETag(string E) {
			return new Instruction ("ETag", String.Format("\"{0}\"", E));
		}
		public override string ToString () {
			string optionsText = String.Empty;
			foreach(string IT in Options) {
				optionsText += " ;" + IT;
			}
			return String.Format ("{0}: {1}{2}{3}", Key, Value, optionsText, HTTP.Newline);
		}
	}
	public class Response {
		protected List<Instruction> httpInstructions = new List<Instruction> ();
		public struct Code {
			public readonly int Number; public readonly string Description;
			public Code(int num, string desc) {
				Number=num;Description=desc;
			}
			public override string ToString () {
				return "HTTP/1.1 "+Number+" "+Description;
			}
			public static readonly Code N100 = new Code(100, "Continue");
			public static readonly Code N200 = new Code(200, "OK");
			public static readonly Code N304 = new Code(304, "Not Modified");
			public static readonly Code N307 = new Code(307, "Temporary Redirect");
			public static readonly Code N400 = new Code(400, "Bad Request");
			public static readonly Code N401 = new Code(401, "Authorization Required");
			public static readonly Code N403 = new Code(403, "Forbidden");
			public static readonly Code N404 = new Code(404, "Not Found");
			public static readonly Code N500 = new Code(500, "Internal Server Error");
			public static readonly Code N501 = new Code(501, "Not Implemented");
		}
		protected Code httpCode;
		protected IStreamableContent httpBody = new GeneratedResource (new byte[]{}, MIME.Plaintext);
		public int MaxAge = 0;
		public Instruction.Connection connectionStatus = Instruction.Connection.KeepAlive;
		public Response(Code C, IStreamableContent body) {
			httpCode = C;
			this.httpBody = body;
			this.AddInstruction (new Instruction("Server", "Sabertooth"));
			this.AddInstruction (new Instruction("Date", DateTime.Now.ToString("R")));
		}
		public Response(Code C) : this(C, null) {
		}
		public void AddInstruction(Instruction I) {
			this.httpInstructions.Add (I);
		}

		public void SetBody(byte[] B, MIME M) {
			this.httpBody = new GeneratedResource (B, M);
		}
		public void SetBody(IStreamableContent ICS) {
			this.httpBody = ICS;
		}

		protected string GetNoContentHeader() {
			string returnString = httpCode.ToString() + HTTP.Newline;
			returnString += (connectionStatus == Instruction.Connection.KeepAlive ? Instruction.ConnectionKeepAlive : Instruction.ConnectionClose);
			foreach(Instruction I in httpInstructions) {
				returnString += I;
			}
			returnString += new Instruction ("Cache-Control", "max-age=" + MaxAge.ToString ());
			return returnString;
		}

		protected string GetFullHeader() {
			string nch = this.GetNoContentHeader ();
			if (httpBody != null) {
				if (httpBody.IsLoaded) {
					byte[] response = httpBody.GetBytes ();
					return nch + Instruction.ContentType(httpBody.Format) + Instruction.ContentLength(response.Length) + HTTP.Newline;
				} else {
					return nch + Instruction.ContentType (httpBody.Format) + Instruction.ContentLength (httpBody.GetSize ()) + HTTP.Newline;
				}
			} else {
				return nch + Instruction.ContentLength (0) + HTTP.Newline;
			}

		}

		public byte[] GetBytes() {
			string responseStr = GetNoContentHeader ();
			byte[] bodybytes = httpBody.GetBytes ();
			if(bodybytes.Length > 0) {
				responseStr += Instruction.ContentType (httpBody.Format);
			}
			responseStr += Instruction.ContentLength (bodybytes.Length);
			byte[] headerBytes = Encoding.UTF8.GetBytes (responseStr + HTTP.Newline);
			if(bodybytes.Length > 0) {
				byte[] responseBytes = new byte[headerBytes.Length + bodybytes.Length];
				Buffer.BlockCopy (headerBytes, 0, responseBytes, 0, headerBytes.Length);
				Buffer.BlockCopy (bodybytes, 0, responseBytes, headerBytes.Length, bodybytes.Length);
				return responseBytes;
			} else {
				return headerBytes;
			}
		}
		public int GetSize() {
			return -1;
		}
		public void StreamHeaderTo(Stream S) {
			string fh = this.GetFullHeader();
			MemoryStream MS = new MemoryStream (Encoding.UTF8.GetBytes(fh));
			MS.CopyTo (S);
			MS.Flush ();
			MS.Close ();
		}
		public void StreamCompleteTo(Stream S) {
			if (httpBody != null) {
				if (httpBody.IsLoaded) {
					byte[] response = this.GetBytes ();
					MemoryStream MS = new MemoryStream (response);
					MS.CopyTo (S);
					MS.Flush ();
					MS.Close ();
				} else {
					string nchead = this.GetNoContentHeader ();
					nchead += Instruction.ContentType (httpBody.Format);
					nchead += Instruction.ContentLength (httpBody.GetSize ());
					byte[] headerbytes = Encoding.UTF8.GetBytes (nchead + HTTP.Newline);
					S.Write (headerbytes, 0, headerbytes.Length);
					httpBody.StreamTo (S);
				}
			} else {
				string ncr = this.GetNoContentHeader () + Instruction.ContentLength(0) + HTTP.Newline;
				MemoryStream MS = new MemoryStream (Encoding.UTF8.GetBytes(ncr));
				MS.CopyTo (S);
				MS.Flush ();
				MS.Close ();
			}
		}
	}

	/*public class EmptyResponse : Response, IStreamable {
		string overrideresponse = null;
		public EmptyResponse(Response.Code C) : base(C, null) {

		}
		public EmptyResponse(Response.Code C, string overrideresponse) : base(C, null) {
			this.overrideresponse = overrideresponse;
		}
		public override byte[] GetBytes() {
			if (overrideresponse == null) {
				string responseStr = GetNoContentHeader (false);
				responseStr += Instruction.ContentLength (0) + Newline;
				byte[] headerBytes = Encoding.UTF8.GetBytes (responseStr + Newline);
				return headerBytes;
			} else {
				return Encoding.UTF8.GetBytes (overrideresponse);
			}
		}
		public override void StreamTo(Stream S) {
			byte[] response = this.GetBytes ();
			MemoryStream MS = new MemoryStream (response);
			MS.CopyTo (S);
			MS.Flush ();
		}
		public override int GetSize() {
			return this.GetBytes().Length;
		}
	}*/
}

