using System.ComponentModel;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Views;
using HVO.RoofControllerV4.iPad.Popups;
using HVO.RoofControllerV4.iPad.ViewModels;
using Microsoft.Maui.ApplicationModel;

namespace HVO.RoofControllerV4.iPad;

public partial class MainPage : ContentPage
{
	private readonly RoofControllerViewModel _viewModel;
	private HealthStatusPopup? _healthStatusPopup;

	public MainPage(RoofControllerViewModel viewModel)
	{
		InitializeComponent();
		_viewModel = viewModel;
		BindingContext = _viewModel;
		_viewModel.PropertyChanged += OnViewModelPropertyChanged;
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		await _viewModel.InitializeAsync();
	}

	private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(RoofControllerViewModel.IsHealthDialogOpen))
		{
			MainThread.BeginInvokeOnMainThread(UpdateHealthPopupState);
		}
	}

	private void UpdateHealthPopupState()
	{
		if (_viewModel.IsHealthDialogOpen)
		{
			if (_healthStatusPopup is not null)
			{
				return;
			}

			var popup = new HealthStatusPopup
			{
				BindingContext = _viewModel
			};
			popup.Closed += OnHealthPopupClosed;
			_healthStatusPopup = popup;

			_ = this.ShowPopupAsync(popup);
		}
		else if (_healthStatusPopup is not null)
		{
			_healthStatusPopup.Close();
		}
	}

	private void OnHealthPopupClosed(object? sender, PopupClosedEventArgs e)
	{
		if (sender is HealthStatusPopup popup)
		{
			popup.Closed -= OnHealthPopupClosed;
		}

		_healthStatusPopup = null;

		if (_viewModel.IsHealthDialogOpen)
		{
			_viewModel.CloseHealthDialogCommand.Execute(null);
		}
	}
}
