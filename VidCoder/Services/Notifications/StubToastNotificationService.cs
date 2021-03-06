﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.UI.Notifications;

namespace VidCoder.Services.Notifications
{
	public class StubToastNotificationService : IToastNotificationService
	{
		public bool ToastEnabled => false;

		public void ShowToast(string toastContent)
		{
		}

		public void Clear()
		{
		}

		public IReadOnlyList<ToastNotification> GetHistory()
		{
			return new List<ToastNotification>();
		}

		public void Remove(string tag)
		{
		}

		public void Remove(string tag, string @group)
		{
		}

		public void RemoveGroup(string @group)
		{
		}
	}
}
