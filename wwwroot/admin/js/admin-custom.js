document.querySelectorAll('.btn-danger').forEach(function(btn) {
    if (btn.querySelector('.bi-trash') || btn.querySelector('.fa-trash')) {
        btn.addEventListener('click', function(e) {
            if (!confirm('Are you sure you want to delete this item?')) {
                e.preventDefault();
            }
        });
    }
});

document.querySelectorAll('form').forEach(function(form) {
    form.addEventListener('submit', function(e) {
        var isValid = true;
        var requiredFields = form.querySelectorAll('[required]');
        
        requiredFields.forEach(function(field) {
            if (!field.value.trim()) {
                isValid = false;
                field.classList.add('is-invalid');
            } else {
                field.classList.remove('is-invalid');
            }
        });
        
        if (!isValid) {
            e.preventDefault();
            alert('Please fill in all required fields.');
        }
    });
});

var tooltipTriggerList = [].slice.call(document.querySelectorAll('[data-bs-toggle="tooltip"]'));
tooltipTriggerList.map(function(tooltipTriggerEl) {
    return new bootstrap.Tooltip(tooltipTriggerEl);
});

console.log('LifeLink Admin Panel Loaded Successfully');
