NSub
=========

NSub is an automated subtitles downloader. It hashes the beginning and end of your media file and accesses [thesubdb api] to check for a guaranteed matching subtitle.

[thesubdb api]: http://thesubdb.com/api/

Configuration
--------------

You are required to modify a few paths in App.config

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <appSettings>
  
    <!-- this can also be a network location such as \\server\shared\path  -->
    <add key="Root.Shows.Path" value="\path\to\your\tv shows" />
    <add key="Root.Movies.Path" value="\path\to\your\movies" />
    
    <!-- Subtitle languages to check for -->
    <add key="Subtitle.Languages" value="en,us" />
    
    <!-- A log of all downloaded subtitles including the .txt -->
    <add key="Log.Downloaded.Path" value="\path\to\your\log.txt" />
    
    <!-- Including the .xml -->
    <add key="Cache.Path" value="\path\to\your\cache.xml" />
  
  </appSettings>
</configuration>
```

License
--------------

```
   Copyright © 2013 Patrick Magee
   
   This program is free software: you can redistribute it and/or modify it
   under the +terms of the GNU General Public License as published by 
   the Free Software Foundation, either version 3 of the License, 
   or (at your option) any later version.
   
   This program is distributed in the hope that it will be useful, 
   but WITHOUT ANY WARRANTY; without even the implied warranty of 
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the 
   GNU General Public License for more details.
   
   You should have received a copy of the GNU General Public License
   along with this program. If not, see http://www.gnu.org/licenses/.
 ```