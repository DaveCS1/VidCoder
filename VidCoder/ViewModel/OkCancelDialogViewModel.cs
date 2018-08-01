﻿using System;
using System.Windows.Input;
using ReactiveUI;
using VidCoder.Services.Windows;

namespace VidCoder.ViewModel
{
	public abstract class OkCancelDialogViewModel : ReactiveObject, IClosableWindow
	{
		private IWindowManager windowManager = Ioc.Get<IWindowManager>();

		protected OkCancelDialogViewModel()
		{
		}

		public virtual bool CanClose
		{
			get
			{
				return true;
			}
		}

		public virtual bool OnClosing()
		{
			return true;
		}

		public bool DialogResult { get; set; }

		private ReactiveCommand cancel;
		public ICommand Cancel
		{
			get
			{
				return this.cancel ?? (this.cancel = ReactiveCommand.Create(() =>
				{
					this.DialogResult = false;
					this.windowManager.Close(this);
				}));
			}
		}

		private ReactiveCommand accept;
		public ICommand Accept
		{
			get
			{
				return this.accept ?? (this.accept = ReactiveCommand.Create(
					() =>
					{
						this.DialogResult = true;
						this.windowManager.Close(this);
					},
					this.WhenAnyValue(x => x.CanClose)));
			}
		}
	}
}
