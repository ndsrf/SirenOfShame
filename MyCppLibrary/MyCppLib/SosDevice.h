//
//  SosDevice.h
//  MyCppLibrary
//
//  Created by Lee Richardson on 4/15/15.
//  Copyright (c) 2015 Lee Richardson. All rights reserved.
//

#ifndef MyCppLibrary_SosDevice_h
#define MyCppLibrary_SosDevice_h

extern "C" void GetHelloCount(void);

extern "C" void ReadLedPatterns(char* ledPatterns, int& ledId);

#endif