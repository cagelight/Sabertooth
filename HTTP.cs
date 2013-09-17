using System;
using System.Collections.Generic;
using System.Text;

using Sabertooth.Lexicon;

namespace Sabertooth {
	public abstract class HTTPObject : IStreamable{
		public const string Newline = "\r\n";
		public class Instruction : ITextable {
			public readonly string Key;
			public ITextable Value;
			private List<ITextable> Options = new List<ITextable>();
			public Instruction(string key, ITextable value) {
				Key = key;Value = value;
			}
			public string GetText() {
				string optionsText = String.Empty;
				foreach(ITextable IT in Options) {
					optionsText += " ;" + IT.GetText ();
				}
				return String.Format ("{0}: {1}{2}", Key, Value.GetText(), optionsText);
			}
			public static Instruction ConnectionClose = new Instruction ("Connection", new GenericTextable("close"));
			public static Instruction ConnectionKeepAlive = new Instruction ("Connection", new GenericTextable("keep-alive"));
			internal static Instruction ContentLength(int length) {
				return new Instruction ("Content-Length", new GenericTextable (length));
			}
			internal static Instruction ContentType(MIME type) {
				return new Instruction ("Content-Type", type);
			}
		}
		protected struct Body : IStreamable {
			public IStreamable Content;
			public MIME Format;
			public Body(IStreamable C, MIME F) {
				Content = C;
				Format = F;
			}
			public byte[] GetBytes() {
				return Content.GetBytes ();
			}
			internal static readonly Body Empty = new Body (new TextStreamable(String.Empty), MIME.Plaintext);
		}
		protected List<Instruction> httpInstructions = new List<Instruction> ();
		public HTTPObject() {

		}

		public abstract byte[] GetBytes ();
	}

	public class Response : HTTPObject, IStreamable {
		public struct Code : ITextable{
			public readonly int Number; public readonly string Description;
			public Code(int num, string desc) {
				Number=num;Description=desc;
			}
			public string GetText () {
				return "HTTP/1.1 "+Number+" "+Description;
			}
			public static readonly Code N100 = new Code(100, "Continue");
			public static readonly Code N200 = new Code(200, "OK");
			public static readonly Code N400 = new Code(400, "Bad Request");
			public static readonly Code N403 = new Code(403, "Forbidden");
			public static readonly Code N404 = new Code(404, "Not Found");
			public static readonly Code N501 = new Code(501, "Not Implemented");
		}
		Code httpCode;
		Body httpBody = Body.Empty;
		public Response(Code C) {
			httpCode = C;
		}

		public void AddInstruction(Instruction I) {
			this.httpInstructions.Add (I);
		}

		public void SetBody(IStreamable B, MIME M) {
			this.httpBody = new Body (B, M);
		}

		public string GetNoContentHeader() {
			string returnString = httpCode.GetText () + Newline;
			foreach(Instruction I in httpInstructions) {
				returnString += I.GetText () + Newline;
			}
			return returnString;
		}

		public override byte[] GetBytes() {
			string responseStr = GetNoContentHeader ();
			byte[] bodyBytes = httpBody.GetBytes ();
			int bodySize = bodyBytes.Length;
			if (bodySize > 0) {
				responseStr += Instruction.ContentType (httpBody.Format).GetText() + Newline;
				responseStr += Instruction.ContentLength (bodySize).GetText() + Newline;
			}
			responseStr += Newline;
			byte[] headerBytes = Encoding.UTF8.GetBytes (responseStr);
			if(bodySize > 0) {
				byte[] responseBytes = new byte[headerBytes.Length + bodyBytes.Length];
				Buffer.BlockCopy (headerBytes, 0, responseBytes, 0, headerBytes.Length);
				Buffer.BlockCopy (bodyBytes, 0, responseBytes, headerBytes.Length, bodyBytes.Length);
				return responseBytes;
			} else {
				return headerBytes;
			}
		}
		public static Response Standard100 { get{
				Response R = new Response (Code.N100);
				R.AddInstruction (Instruction.ConnectionKeepAlive);
				return R;
			}
		}
		public static Response Standard400 { get{
				Response R = new Response (Code.N400);
				R.AddInstruction (Instruction.ConnectionClose);
				return R;
			}
		}
	}
}

