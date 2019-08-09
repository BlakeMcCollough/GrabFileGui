Author: Blake McCollough
Contact: blakemccollough@yahoo.com
Description:
	GrabFileGui.exe reads and displays readable information from grab files. The purpose is to help visualize grab
	information. When the application starts, it attempts to communicate with GrabDriver.dll where GrabDriver will
	simulate live grab data and feed it to GrabFileGui.exe. If GrabDriver.dll fails to send data after 5 seconds,
	the GrabFileGui.exe will timeout. It is still possible to read GrabFile manually where it will read the entire
	file for 30 seconds (1 second is artificially added between steps). Every second, data will be updated/added
	to the graph for each task. The task tab displays the contents of the grab file for each second and updates
	accordingly. Anytime there's disk usage happening, it is logged under the disk tab. The startup information is
	recorded under the startup tab. The total CPU usage is recorded under performance tab. Anytime the application
	does something, it is logged in a GrabHistory folder with the same path as the .exe. Within the GrabHistory directory,
	each day the application is being used is saved as mmddyyyy, where live Grab data is saved in. After a day has passed,
	the directory is zipped to conserve space. If a week has passed and a folder has not been used, the folder will be
	automatically deleted without warning. The update speed for each second may be adjusted under the view menu. If
	live data is being read, the speed cannot change, however it may still be paused.