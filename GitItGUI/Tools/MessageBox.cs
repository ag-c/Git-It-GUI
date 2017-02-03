﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

#if WINDOWS
using Win = System.Windows.Forms;
#endif

namespace GitItGUI
{
	enum MessageBoxTypes
	{
		Ok,
		OkCancel,
		YesNo
	}

	static class MessageBox
	{
		public static bool Show(string text)
		{
			return Show("Alert", text, MessageBoxTypes.Ok);
		}

		public static bool Show(string text, MessageBoxTypes type)
		{
			return Show("Alert", text, type);
		}

		public static bool Show(string message, string title, MessageBoxTypes type)
		{
			string result;
			return Tools.CoreApps.LaunchMessageBox(title, message, out result);

			#if WINDOWS
			//Win.DialogResult result = Win.DialogResult.None;
			//switch (type)
			//{
			//	case MessageBoxTypes.Ok: result = Win.MessageBox.Show(title, message, Win.MessageBoxButtons.OK); break;
			//	case MessageBoxTypes.OkCancel: result = Win.MessageBox.Show(title, message, Win.MessageBoxButtons.OKCancel); break;
			//	case MessageBoxTypes.YesNo: result = Win.MessageBox.Show(title, message, Win.MessageBoxButtons.YesNo); break;
			//}

			//if (result == Win.DialogResult.OK || result == Win.DialogResult.Yes) return true;
			//return false;
			#endif
		}
	}
}
