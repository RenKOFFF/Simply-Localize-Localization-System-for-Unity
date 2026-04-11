mergeInto(LibraryManager.library, {
    
    SimplyLocalize_ConsoleLog: function(levelPtr, prefixColorPtr, messagePtr, argsJsonPtr) {
        var level = UTF8ToString(levelPtr);
        var prefixColor = UTF8ToString(prefixColorPtr);
        var message = UTF8ToString(messagePtr);
        var argsJson = UTF8ToString(argsJsonPtr);
        
        var prefix = "%c[SimplyLocalize]%c ";
        var prefixStyles = [
            "color: " + prefixColor + "; font-weight: bold;",
            "color: inherit;"
        ];
        
        var finalMessage = prefix;
        var styles = prefixStyles.slice();
        
        if (argsJson && argsJson.length > 2) {
            try {
                var args = JSON.parse(argsJson);
                var argIndex = 0;
                
                finalMessage += message.replace(/\{(\d+)\}/g, function(match, index) {
                    var idx = parseInt(index);
                    if (idx < args.length) {
                        var arg = args[idx];
                        styles.push("color: " + arg.color + "; font-weight: bold;");
                        styles.push("color: inherit;");
                        return "%c" + arg.text + "%c";
                    }
                    return match;
                });
            } catch (e) {
                finalMessage += message;
            }
        } else {
            finalMessage += message;
        }
        
        var logArgs = [finalMessage].concat(styles);
        
        if (level === "Warning") {
            console.warn.apply(console, logArgs);
        } else if (level === "Error") {
            console.error.apply(console, logArgs);
        } else {
            console.log.apply(console, logArgs);
        }
    }
});
