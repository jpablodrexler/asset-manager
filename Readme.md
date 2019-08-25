# JPPhotoManager

![JPPhotoManager](JPPhotoManager/Images/AppIcon.png)

[![Build Status](https://dev.azure.com/jpablodrexler/jp-photo-manager/_apis/build/status/jpablodrexler.jp-photo-manager?branchName=master)](https://dev.azure.com/jpablodrexler/jp-photo-manager/_build/latest?definitionId=10&branchName=master)

## Trello board
https://trello.com/b/7OlQJdBw/jp-photo-manager

## Features
JPPhotoManager is a desktop application that allows:
* Visualization of image galleries
* Change Windows wallpaper
* Find duplicates
* Copy/move images

Soon will allow to:
* Import images from game screenshots folder
* Add your own metadata to the images
* Search images

## Run the application
Open the solution file `JPPhotoManager/JPPhotoManager.sln` and run the `JPPhotoManager/JPPhotoManager/JPPhotoManager.csproj` project.

## Installation instructions
* Unzip the content of the zip file to a new folder.
* Run `JPPhotoManager.exe`.
* The application builds the catalog files in the following folder: `C:\Users\{username}\AppData\Local\JPPhotoManager`.

## Technologies used
* Windows Presentation Foundation
* [Simple Injector](https://simpleinjector.org/index.html)
* [Newtonsoft Json.NET](https://www.newtonsoft.com/json)
* [Moq framework for .NET](https://github.com/moq/moq4)
* [log4net](https://logging.apache.org/log4net/)
