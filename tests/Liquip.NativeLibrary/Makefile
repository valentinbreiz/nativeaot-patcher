# 'make'        build shared library
# 'make clean'  removes all .o and shared library files
#

# define the C++ compiler to use
CXX = g++

# define any compile-time flags
CXXFLAGS := -std=c++17 -Wall -Wextra -g 

# define library paths in addition to /usr/lib
#   if you want to include libraries not in /usr/lib, specify their path with -Lpath
LFLAGS =

# define output directory
OUTPUT := output

# define source directory
SRC := src

# define include directory
INCLUDE := include

# define lib directory
LIB := lib

ifeq ($(OS),Windows_NT)
LIBRARY := LiquipNativeLibrary.dll
SOURCEDIRS := $(SRC)
INCLUDEDIRS := $(INCLUDE)
LIBDIRS := $(LIB)
FIXPATH = $(subst /,\,$1)
RM := del /q /f
MD := mkdir
else
LIBRARY := LiquipNativeLibrary.so
SOURCEDIRS := $(shell find $(SRC) -type d)
INCLUDEDIRS := $(shell find $(INCLUDE) -type d)
LIBDIRS := $(shell find $(LIB) -type d)
FIXPATH = $1
RM = rm -f
MD := mkdir -p
endif

# define any directories containing header files other than /usr/include
INCLUDES := $(patsubst %,-I%, $(INCLUDEDIRS:%/=%))

# define the C libs
LIBS := $(patsubst %,-L%, $(LIBDIRS:%/=%)) -ldl

# define the C source files
SOURCES := $(wildcard $(patsubst %,%/*.cpp, $(SOURCEDIRS)))

# define the C object files
OBJECTS := $(SOURCES:.cpp=.o)

# define the dependency output files
DEPS := $(OBJECTS:.o=.d)

#
# The following part of the makefile is generic; it can be used to
# build any shared library just by changing the definitions above.
#

OUTPUTLIBRARY := $(call FIXPATH,$(OUTPUT)/$(LIBRARY))

all: $(OUTPUT) $(LIBRARY)
	@echo Executing 'all' complete!

$(OUTPUT):
	$(MD) $(OUTPUT)

$(LIBRARY): $(OBJECTS)
	$(CXX) $(CXXFLAGS) $(INCLUDES) -o $(OUTPUTLIBRARY) $(OBJECTS) $(LFLAGS) $(LIBS) -shared -fPIC -export-dynamic

# include all .d files
-include $(DEPS)

# this is a suffix replacement rule for building .o's and .d's from .cpp's
# it uses automatic variables $<: the name of the prerequisite of
# the rule(a .cpp file) and $@: the name of the target of the rule (a .o file)
# -MMD generates dependency output files same name as the .o file
# (see the gnu make manual section about automatic variables)
.cpp.o:
	$(CXX) $(CXXFLAGS) $(INCLUDES) -c -MMD $< -o $@

.PHONY: clean
clean:
	$(RM) $(OUTPUTLIBRARY)
	$(RM) $(call FIXPATH,$(OBJECTS))
	$(RM) $(call FIXPATH,$(DEPS))
	@echo Cleanup complete!
