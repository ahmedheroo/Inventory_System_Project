// The console ships one script. Anything a screen needs is either server-rendered or
// the browser's own behaviour (date pickers, form validation, printing). The one thing
// the browser cannot ask for is "did you mean it": a form marked data-confirm gets a
// question, and the answer is the only thing that submits it.
document.addEventListener('submit', function (event) {
    var form = event.target;

    if (!(form instanceof HTMLFormElement) || !form.dataset.confirm) {
        return;
    }

    if (!window.confirm(form.dataset.confirm)) {
        event.preventDefault();
    }
});
