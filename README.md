# SerialConsoleUtility
Serial Port Utility For Makers

This app can be used to communicate over serial ports. Runs with .Net5. Uses WPF as UI so only supported by Windows OS'es. The main purpose is to help develop firmware like arduino and similar. 

# Usage
## Connection
The app detects serial ports of the computer and, shows them on the list.

![Connection](https://raw.githubusercontent.com/mgulsoy/SerialConsoleUtility/master/readme_content/connection.jpg) ![Connected](https://raw.githubusercontent.com/mgulsoy/SerialConsoleUtility/master/readme_content/connected.jpg)

Select a port from the `Port` List. Select other parameters like `Baud`, `Data Bytes` and `Parity` then click `Open`. The app remembers these settings except port the next time when you run it.

## Sending and Receiving Data
When the connection is open you can send and receive data. Type in the data you want to send in the `Compose` box and click `Send` button. You can monitor the traffic in the `Traffic` box. Received data is indicated with Green and Sent data is indicated with Pink. Also when you hover your mouse on traffic data, it is emphasized. 

![Compose and send](https://raw.githubusercontent.com/mgulsoy/SerialConsoleUtility/master/readme_content/compose_and_send.jpg)

## Escaping Data
The UI and app uses UTF8 encoding when sending and receiving data. But what if you need to send some values which you cannot type? To do that you can escape data with some formats. The escape char is `#`. When you want to write '#' then type '##'

**Formats:**

**Hex Values**
  *  Format is #xNN
  *  NN is the hex number.
  *  It should be exactly 2 valid hex chars.
  *  For example if you want to escape the value `0x03` then type `#x03`
  *  For the value `0xC8` type `#xc8` or `#xC8` 
  *  Escaping hex data is **Case-Insensitive**.

**Decimal Values**
  *  Format is #dNNN
  *  NNN is the decimal number. 
  *  The decimal number is bound to the byte size and it is unsigned. Which means min value is 0 and max value is 255.
  *  It should be exactly 3 chars (only digits)
  *  For example if you want to escape the value `5` then type `#d005`
  *  For the value `134` type `#d134`
  *  Typing a number out of byte bounds will yield nothing! So be careful.

**Octal Values**
  *  Format is #oNNN
  *  NNN is the octal number.
  *  It should be exactly 3 chars (digits only and in base 8)
  *  For example if you want to escape the value `4` then type `#o004`
  *  For the value `314` type `#o314`
  *  Typing a number out of byte bounds will yield nothing! So be careful.

## Saving Data as Commands
You can save the data you want to send to use at a later time. To do that just type the command in the `Compose` box and click `Save` button.

![Save Commands](https://raw.githubusercontent.com/mgulsoy/SerialConsoleUtility/master/readme_content/save_command.jpg)

You can operate with a saved command by selecting and right clicking on them. Also double clicking on a command, sends it.

![Saved Command Menu](https://raw.githubusercontent.com/mgulsoy/SerialConsoleUtility/master/readme_content/saved_command_menu.jpg)

You can edit a saved command when the `Compose` box is empty.
Saved commands can be sent repeatedly. To do that select a command from the list, right click on it and click `Repeat`. Then repeat dialog appears and asks how many times it will repeat and the interval between each send. You should not enter values below 15 ms because of the operating system. The values below 15 ms are not guaranteed.




