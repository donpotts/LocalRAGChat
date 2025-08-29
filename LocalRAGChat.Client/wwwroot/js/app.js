function scrollToElementBottom(elementId) {
    const element = document.getElementById(elementId);
    if (element) {
        // Use smooth scrolling for better user experience
        element.scrollTo({
            top: element.scrollHeight,
            behavior: 'smooth'
        });
    }
}

// Alternative function for immediate scrolling (no animation)
function scrollToElementBottomImmediate(elementId) {
    const element = document.getElementById(elementId);
    if (element) {
        element.scrollTop = element.scrollHeight;
    }
}